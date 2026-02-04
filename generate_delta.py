import numpy as np
import os

def generate_unity_indexed_deltas(asset_dir, output_dir):
    if not os.path.exists(output_dir): os.makedirs(output_dir)

    def load_safe_asset(f_num, path):
        prefix = f"4DGS-dynerf_coffee_martini-{f_num}-point_cloud"
        ids = np.fromfile(os.path.join(path, f"{prefix}_ids.bytes"), dtype=np.int32)
        num_points = len(ids)
        with open(os.path.join(path, f"{prefix}_pos.bytes"), "rb") as f:
            raw_data = f.read()
            # float32(12B) 또는 float16(6B) 자동 판별
            if len(raw_data) < num_points * 12:
                pos = np.frombuffer(raw_data[:num_points*6], dtype=np.float16).astype(np.float32).reshape(num_points, 3)
            else:
                pos = np.frombuffer(raw_data[:num_points*12], dtype=np.float32).reshape(num_points, 3)
        return ids, pos

    # 1. 기준: 유니티 0001 에셋의 물리적 저장 순서
    base_ids, base_pos = load_safe_asset("0001", asset_dir)
    base_map = dict(zip(base_ids, base_pos))

    curr_idx = 2
    while curr_idx <= 300:
        f_num = f"{curr_idx:04d}"
        p = os.path.join(asset_dir, f"4DGS-dynerf_coffee_martini-{f_num}-point_cloud_pos.bytes")
        if not os.path.exists(p):
            curr_idx += 1
            continue

        print(f"Frame {f_num}: Generating 32-byte aligned delta...")
        curr_ids, curr_pos = load_safe_asset(f_num, asset_dir)
        curr_map = dict(zip(curr_ids, curr_pos))
        
        # 0001 에셋 순서에 맞춰 현재 좌표 재배열
        reordered_pos = np.array([curr_map.get(tid, base_map[tid]) for tid in base_ids])
        delta_pos = reordered_pos - base_pos
        
        # [핵심] 32바이트 규격 생성 (float 8개)
        # 0~2: posDelta, 3: 패딩, 4~7: rotDelta(0으로 채움)
        final_buffer = np.zeros((len(base_ids), 8), dtype=np.float32)
        final_buffer[:, 0:3] = delta_pos
        
        final_buffer.tofile(os.path.join(output_dir, f"frame_{f_num}.delta"))
        curr_idx += 1

generate_unity_indexed_deltas(
    'C:\\Users\\jeong\\GS_Project\\projects\\GaussianExample\\Assets\\GaussianAssets', 
    'C:\\Users\\jeong\\GS_Project\\projects\\GaussianExample\\Assets\\DeltaOutput'
)