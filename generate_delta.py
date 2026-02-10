import numpy as np
import os

def sanitize_data(arr):
    """NaN이나 무한대 값을 0으로 치환하여 에러 방지"""
    if not np.isfinite(arr).all():
        return np.nan_to_num(arr, nan=0.0, posinf=0.0, neginf=0.0)
    return arr

def load_asset_data(f_num, asset_dir):
    """ids.bytes를 포함하여 데이터를 안전하게 로드합니다."""
    prefix = f"4DGS-dynerf_coffee_martini-{f_num}-point_cloud"
    
    paths = {
        'ids': os.path.join(asset_dir, f"{prefix}_ids.bytes"),
        'pos': os.path.join(asset_dir, f"{prefix}_pos.bytes"),
        'oth': os.path.join(asset_dir, f"{prefix}_oth.bytes"),
        'col': os.path.join(asset_dir, f"{prefix}_col.bytes")
    }
    
    # 필수 파일 확인
    for p in paths.values():
        if not os.path.exists(p): return None

    # 1. 원본 데이터 읽기
    ids_raw = np.fromfile(paths['ids'], dtype=np.int32)
    pos_raw = np.fromfile(paths['pos'], dtype=np.float32)
    oth_raw = np.fromfile(paths['oth'], dtype=np.float32)
    col_raw = np.fromfile(paths['col'], dtype=np.uint8)
    
    # 2. 공통 개수(n) 계산 (파일 끝 패딩 무시)
    n_ids = len(ids_raw)
    n_pos = len(pos_raw) // 3
    n = min(n_ids, n_pos) # ID와 좌표 개수 중 작은 쪽 기준

    # 3. 데이터 자르기 및 성형
    ids = ids_raw[:n]
    
    pos = pos_raw[:n*3].reshape(n, 3)
    pos = sanitize_data(pos) # NaN 제거

    rot = oth_raw[:n*4].reshape(n, 4)
    rot = sanitize_data(rot)

    # Opacity 추출 (Stride 방식)
    bytes_per_col = len(col_raw) // n_ids if n_ids > 0 else 16
    opac = col_raw[np.arange(n) * bytes_per_col + 3].astype(np.float32) / 255.0
    opac = sanitize_data(opac)

    return ids, pos, rot, opac

def generate_delta_by_id_lookup(asset_dir, output_dir):
    if not os.path.exists(output_dir): os.makedirs(output_dir)

    # --- Step 1: 기준 프레임(0001) 로드 ---
    print("Step 1: 기준 프레임(0001) 로드 중...")
    base_data = load_asset_data("0001", asset_dir)
    if not base_data:
        print("에러: 0001 프레임 데이터를 찾을 수 없습니다.")
        return

    b_ids, b_pos, b_rot, b_opac = base_data
    n_base = len(b_ids)
    print(f"기준 가우시안 개수: {n_base}")

    # --- Step 2: 프레임 순회 및 델타 생성 ---
    # 2번 프레임부터 300번(예시)까지
    for f_idx in range(2, 301): 
        f_num = f"{f_idx:04d}"
        
        curr_data = load_asset_data(f_num, asset_dir)
        if not curr_data: continue

        c_ids, c_pos, c_rot, c_opac = curr_data
        
        # [핵심] 검색 속도를 위해 Dictionary(해시맵) 생성
        # Key: Vertex ID, Value: Index (현재 프레임에서의 몇 번째 줄인지)
        # 이렇게 하면 ID로 즉시 위치를 찾을 수 있습니다.
        curr_id_map = {uid: i for i, uid in enumerate(c_ids)}

        # 48바이트 델타 버퍼 (0001 기준 순서 고정)
        # [0:3]Pos, [3:7]Rot, [7:10]Scale(0), [10]Opac, [11]Pad
        delta_buffer = np.zeros((n_base, 12), dtype=np.float32)
        
        match_count = 0
        for i in range(n_base):
            target_id = b_ids[i] # 0001 프레임 i번째 칸의 ID
            
            # 현재 프레임에 그 ID가 존재하는지 확인
            if target_id in curr_id_map:
                c_idx = curr_id_map[target_id] # 현재 프레임에서의 인덱스
                
                # [계산] 현재 값 - 기준 값
                diff_pos = c_pos[c_idx] - b_pos[i]
                diff_rot = c_rot[c_idx] - b_rot[i]
                diff_opac = c_opac[c_idx] - b_opac[i]

                # ==========================================
                # [좌표계 보정] Z축 반전 (Unity Coordinate Fix)
                # ==========================================
                diff_pos[2] *= -1 
                # 필요 시: diff_pos[0] *= -1 (X축 반전)

                delta_buffer[i, 0:3] = diff_pos
                delta_buffer[i, 3:7] = diff_rot
                delta_buffer[i, 10] = diff_opac
                match_count += 1
        
        # 결과 저장
        output_path = os.path.join(output_dir, f"frame_{f_num}.delta")
        delta_buffer.tofile(output_path)
        
        if f_idx % 1 == 0:
            print(f"프레임 {f_num} 생성 완료: {match_count}/{n_base} 개 매칭됨")

    print("\n[완료] ID 매칭을 통한 정밀 델타 생성이 끝났습니다.")

# 실행
generate_delta_by_id_lookup(
    asset_dir='C:/Users/jeong/GS_Project/projects/GaussianExample/Assets/GaussianAssets',
    output_dir='C:/Users/jeong/GS_Project/projects/GaussianExample/Assets/DeltaOutput'
)