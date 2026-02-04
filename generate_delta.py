import numpy as np
import os

def generate_unity_indexed_deltas_48b(asset_dir, output_dir):
    if not os.path.exists(output_dir): 
        os.makedirs(output_dir)

    def load_safe_asset(f_num, path):
        # 유니티 에셋 파일명 규칙에 맞춤
        prefix = f"4DGS-dynerf_coffee_martini-{f_num}-point_cloud"
        id_path = os.path.join(path, f"{prefix}_ids.bytes")
        pos_path = os.path.join(path, f"{prefix}_pos.bytes")
        
        # 1. ID 로드
        ids = np.fromfile(id_path, dtype=np.int32)
        num_points = len(ids)
        
        # 2. Position 로드 (float32 또는 float16 대응)
        with open(pos_path, "rb") as f:
            raw_data = f.read()
            expected_32bit_bytes = num_points * 12
            
            if len(raw_data) < expected_32bit_bytes:
                # float16 압축인 경우
                pos = np.frombuffer(raw_data[:num_points*6], dtype=np.float16).astype(np.float32).reshape(num_points, 3)
            else:
                # 일반 float32인 경우
                pos = np.frombuffer(raw_data[:expected_32bit_bytes], dtype=np.float32).reshape(num_points, 3)
                
        return ids, pos

    print("기준 프레임(0001) 로드 중... (유니티 인덱스 순서 확보)")
    # [중요] 유니티 0001 에셋의 물리적 저장 순서를 기준으로 삼음
    base_ids, base_pos = load_safe_asset("0001", asset_dir)
    base_map = dict(zip(base_ids, base_pos))

    # 처리할 프레임 범위 설정
    for f_idx in range(2, 301):
        f_num = f"{f_idx:04d}"
        target_pos_file = os.path.join(asset_dir, f"4DGS-dynerf_coffee_martini-{f_num}-point_cloud_pos.bytes")
        
        if not os.path.exists(target_pos_file):
            continue

        print(f"프레임 {f_num} 처리 중: 48바이트 규격 생성...")
        
        try:
            curr_ids, curr_pos = load_safe_asset(f_num, asset_dir)
            curr_map = dict(zip(curr_ids, curr_pos))
            
            # 0001 에셋의 ID 순서 그대로 현재 좌표 재배치 (인덱스 동기화)
            reordered_curr_pos = np.array([curr_map.get(tid, base_map[tid]) for tid in base_ids])
            
            # 델타(변화량) 계산
            delta_pos = reordered_curr_pos - base_pos
            
            # [핵심] 유니티 Custom 구조체 규격(48바이트) 생성
            # float 12개 = 48바이트 (posDelta 3개 + pad0 1개 + rotDelta 4개 + opacityDelta 1개 + pad1 3개)
            final_delta_buffer = np.zeros((len(base_ids), 12), dtype=np.float32)
            
            # 앞의 3칸(0, 1, 2)에 좌표 변화량 채우기
            final_delta_buffer[:, 0:3] = delta_pos
            
            # 나머지 4~11번 칸은 0으로 채워진 상태 유지 (회전/투명도 변화 없음)
            
            # 바이너리 파일로 저장
            output_path = os.path.join(output_dir, f"frame_{f_num}.delta")
            final_delta_buffer.tofile(output_path)
            
        except Exception as e:
            print(f"프레임 {f_num} 에러: {e}")

    print("완료: 48바이트 정렬 델타 생성이 끝났습니다!")

# 실행 경로 설정 (본인 환경에 맞게 수정)
generate_unity_indexed_deltas_48b(
    asset_dir='C:\\Users\\jeong\\GS_Project\\projects\\GaussianExample\\Assets\\GaussianAssets', 
    output_dir='C:\\Users\\jeong\\GS_Project\\projects\\GaussianExample\\Assets\\DeltaOutput'
)