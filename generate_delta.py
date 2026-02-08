import numpy as np
import os
from scipy.spatial import cKDTree

def generate_asset_to_asset_deltas(asset_dir, output_dir):
    if not os.path.exists(output_dir): 
        os.makedirs(output_dir)

    def load_bytes_safely(f_num, path):
        prefix = f"4DGS-dynerf_coffee_martini-{f_num}-point_cloud"
        
        # 파일 존재 확인
        pos_p = os.path.join(path, f"{prefix}_pos.bytes")
        if not os.path.exists(pos_p): return None

        # 1. Position 로드 (12바이트씩 끊어서 읽기)
        # 파일 전체 크기를 가우시안 개수 n으로 자동 계산
        pos_raw = np.fromfile(pos_p, dtype=np.float32)
        n = len(pos_raw) // 3
        pos = pos_raw[:n*3].reshape(n, 3)

        # 2. Rotation 로드 (float32 * 4 = 16바이트 기준)
        oth_raw = np.fromfile(os.path.join(path, f"{prefix}_oth.bytes"), dtype=np.float32)
        # n개보다 데이터가 많으면 자르고, 부족하면 부족한 대로 n에 맞춰 재정의
        n_oth = len(oth_raw) // 4
        n_final = min(n, n_oth)
        rot = oth_raw[:n_final*4].reshape(n_final, 4)

        # 3. Opacity 로드 (수동 인덱싱으로 reshape 에러 원천 차단)
        col_raw = np.fromfile(os.path.join(path, f"{prefix}_col.bytes"), dtype=np.uint8)
        bytes_per_splat = len(col_raw) // n if n > 0 else 0
        
        if bytes_per_splat >= 4:
            indices = np.arange(n_final) * bytes_per_splat + 3
            opac = col_raw[indices].astype(np.float32) / 255.0
        else:
            opac = np.zeros(n_final, dtype=np.float32)

        return pos[:n_final], rot, opac, n_final

    print("기준 에셋(0001) 로드 중...")
    base_data = load_bytes_safely("0001", asset_dir)
    if not base_data: return
    b_pos, b_rot, b_opac, n_base = base_data

    # 공간 대조를 위한 KD-Tree 구축 (기준 0001 에셋)
    print("공간 대조용 트리 생성 중 (0001 에셋 기준)...")
    base_tree = cKDTree(b_pos)

    for f_idx in range(2, 301):
        f_num = f"{f_idx:04d}"
        target_data = load_bytes_safely(f_num, asset_dir)
        if not target_data: continue

        t_pos, t_rot, t_opac, n_target = target_data
        print(f"프레임 {f_num} 직접 대조 중 (알갱이 수: {n_target})...")

        # [핵심 로직] .asset 직접 비교 (Nearest Neighbor)
        # 0001 에셋의 각 슬롯에 채울 데이터를 현재 에셋에서 가장 가까운 좌표를 가진 놈으로 찾아옴
        _, idx_in_target = cKDTree(t_pos).query(b_pos, k=1)

        # 유니티 48바이트 델타 버퍼 (0001 에셋 크기 고정)
        # [0:3]pos, [3:7]rot, [7:10]scale, [10]opac, [11]pad
        delta_buffer = np.zeros((n_base, 12), dtype=np.float32)

        # 0001 에셋 순서(b_pos)에 맞춰 현재 에셋(t_pos[idx_in_target])의 수치 차이만 계산
        delta_buffer[:, 0:3] = t_pos[idx_in_target] - b_pos
        delta_buffer[:, 3:7] = t_rot[idx_in_target] - b_rot
        delta_buffer[:, 10] = t_opac[idx_in_target] - b_opac

        output_path = os.path.join(output_dir, f"frame_{f_num}.delta")
        delta_buffer.tofile(output_path)

    print("\n[성공] .asset끼리 공간 매칭을 통해 직접 비교를 마쳤습니다.")

# 실행
generate_asset_to_asset_deltas(
    asset_dir='C:/Users/jeong/GS_Project/projects/GaussianExample/Assets/GaussianAssets', 
    output_dir='C:/Users/jeong/GS_Project/projects/GaussianExample/Assets/DeltaOutput'
)