import numpy as np
import os

def inspect_delta_file(file_path):
    if not os.path.exists(file_path):
        print(f"파일을 찾을 수 없습니다: {file_path}")
        return

    print(f"Analyzing: {os.path.basename(file_path)}")
    print("-" * 50)

    # 1. 파일 로드 (float32, 12개씩 끊어서)
    try:
        data = np.fromfile(file_path, dtype=np.float32).reshape(-1, 12)
    except ValueError:
        raw_size = os.path.getsize(file_path)
        print(f"⚠️ 파일 크기({raw_size} bytes)가 48바이트(float*12)로 나누어 떨어지지 않습니다!")
        print(f"예상 가우시안 개수: {raw_size / 48}")
        return

    num_splats = len(data)
    print(f"총 가우시안 개수: {num_splats:,}")

    # 2. 데이터 분리
    # [0:3] Pos, [3:7] Rot, [7:10] Scale, [10] Opac, [11] Pad
    pos_delta = data[:, 0:3]
    rot_delta = data[:, 3:7]
    scale_delta = data[:, 7:10]
    opac_delta = data[:, 10]

    # 3. 통계 분석 함수
    def print_stats(name, arr):
        _min = np.min(arr, axis=0)
        _max = np.max(arr, axis=0)
        _mean = np.mean(arr, axis=0)
        _abs_mean = np.mean(np.abs(arr), axis=0)
        
        print(f"[{name}]")
        print(f"  Min: {_min}")
        print(f"  Max: {_max}")
        print(f"  Avg(Abs): {_abs_mean} (변화량의 평균 크기)")
        
        # 튀는 값 확인 (Position 기준 5유닛 이상 움직이면 경고)
        if name == "Position":
            huge_moves = np.sum(np.linalg.norm(arr, axis=1) > 5.0)
            if huge_moves > 0:
                print(f"  ⚠️ 경고: 5.0 유닛 이상 이동한 스플랫이 {huge_moves}개 있습니다. (좌표계 스케일 확인 필요)")

    # 4. 분석 결과 출력
    print_stats("Position Delta (XYZ)", pos_delta)
    print("-" * 20)
    print_stats("Rotation Delta (XYZW)", rot_delta)
    print("-" * 20)
    print_stats("Opacity Delta", opac_delta)
    
    # 5. 샘플 데이터 출력 (값이 있는 것 위주로)
    print("-" * 50)
    print("--- 0이 아닌 유효 데이터 샘플 (상위 5개) ---")
    
    # 움직임이 조금이라도 있는 인덱스 찾기
    moved_indices = np.where(np.linalg.norm(pos_delta, axis=1) > 0.0001)[0]
    
    if len(moved_indices) > 0:
        for i in moved_indices[:5]:
            print(f"Idx [{i}]:")
            print(f"  Pos: {pos_delta[i]}")
            print(f"  Rot: {rot_delta[i]}")
            print(f"  Opac: {opac_delta[i]}")
    else:
        print("⚠️ 모든 델타 값이 0입니다! (매칭 실패 혹은 변화 없음)")

    # 6. NaN / Inf 체크
    if not np.isfinite(data).all():
        print("\n❌ 델타 파일에 NaN(숫자 아님) 혹은 Inf(무한대) 값이 포함되어 있습니다!")
    else:
        print("\n✅ 데이터 무결성 확인 (NaN/Inf 없음)")

# =========================================================
# 분석할 파일 경로를 입력하세요
# =========================================================
target_delta_file = 'C:/Users/jeong/GS_Project/projects/GaussianExample/Assets/Deltas/frame_0003.delta'

inspect_delta_file(target_delta_file)