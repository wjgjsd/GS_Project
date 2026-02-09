import numpy as np
from plyfile import PlyData
from scipy.spatial import cKDTree
import os
import glob

def find_ply_file(frame_path):
    pattern = os.path.join(frame_path, "**", "*.ply")
    files = glob.glob(pattern, recursive=True)
    if files:
        pc_files = [f for f in files if "point_cloud.ply" in f.lower()]
        return max(pc_files if pc_files else files, key=os.path.getmtime)
    return None

def load_unity_asset_force(f_num, asset_dir):
    """파일 크기 불일치 무시하고 데이터 로드"""
    prefix = f"4DGS-dynerf_coffee_martini-{f_num}-point_cloud"
    
    paths = {
        'ids': os.path.join(asset_dir, f"{prefix}_ids.bytes"),
        'pos': os.path.join(asset_dir, f"{prefix}_pos.bytes"),
        'oth': os.path.join(asset_dir, f"{prefix}_oth.bytes"),
        'col': os.path.join(asset_dir, f"{prefix}_col.bytes")
    }
    for p in paths.values():
        if not os.path.exists(p): return None

    ids_raw = np.fromfile(paths['ids'], dtype=np.int32)
    pos_raw = np.fromfile(paths['pos'], dtype=np.float32)
    oth_raw = np.fromfile(paths['oth'], dtype=np.float32)
    col_raw = np.fromfile(paths['col'], dtype=np.uint8)
    
    n_ids = len(ids_raw)
    n_pos = len(pos_raw) // 3
    n_oth = len(oth_raw) // 4
    n_col = len(col_raw) // 16 
    n = min(n_ids, n_pos, n_oth, n_col)

    pos = pos_raw[:n*3].reshape(n, 3)
    rot = oth_raw[:n*4].reshape(n, 4)
    bytes_per_col = len(col_raw) // n_ids if n_ids > 0 else 16
    opac = col_raw[np.arange(n) * bytes_per_col + 3].astype(np.float32) / 255.0

    return pos, rot, opac, n

def generate_incremental_delta(asset_dir, frames_root, output_dir):
    if not os.path.exists(output_dir): os.makedirs(output_dir)

    print("Step 1: 기준 데이터(0001) 준비 중...")
    base_unity = load_unity_asset_force("0001", asset_dir)
    b_ply_path = find_ply_file(os.path.join(frames_root, "0001"))
    
    if not base_unity or not b_ply_path: return

    b_u_pos, b_u_rot, b_u_opac, n_base = base_unity
    
    # 0001의 ID 매핑 (유니티 슬롯 -> Vertex ID)
    ply_b = PlyData.read(b_ply_path)['vertex']
    b_p_pos = np.stack([ply_b['x'], ply_b['y'], ply_b['z']], axis=1).astype(np.float32)
    b_p_ids = ply_b['vertex_id'].astype(np.int32)

    tree_b = cKDTree(b_p_pos)
    _, indices_b = tree_b.query(b_u_pos, k=1)
    base_unity_vids = b_p_ids[indices_b]

    # [핵심] 이전 프레임의 데이터를 저장할 변수 초기화 (처음엔 0001 데이터)
    # {Vertex_ID : (Pos, Rot, Opac)}
    prev_map = {vid: (b_u_pos[i], b_u_rot[i], b_u_opac[i]) for i, vid in enumerate(base_unity_vids)}

    # 프레임 순회
    frame_folders = sorted([d for d in os.listdir(frames_root) if d.isdigit() and int(d) > 1])
    
    for f_folder in frame_folders:
        f_num = f"{int(f_folder):04d}"
        
        curr_unity = load_unity_asset_force(f_num, asset_dir)
        curr_ply_path = find_ply_file(os.path.join(frames_root, f_folder))
        
        if not curr_unity or not curr_ply_path: continue

        c_u_pos, c_u_rot, c_u_opac, _ = curr_unity
        ply_c = PlyData.read(curr_ply_path)['vertex']
        c_p_pos = np.stack([ply_c['x'], ply_c['y'], ply_c['z']], axis=1).astype(np.float32)
        c_p_ids = ply_c['vertex_id'].astype(np.int32)

        # 현재 프레임 ID 매핑
        tree_c = cKDTree(c_p_pos)
        _, indices_c = tree_c.query(c_u_pos, k=1)
        c_unity_vids = c_p_ids[indices_c]

        # 현재 데이터를 맵으로 변환
        curr_map = {vid: (c_u_pos[i], c_u_rot[i], c_u_opac[i]) for i, vid in enumerate(c_unity_vids)}

        delta_buffer = np.zeros((n_base, 12), dtype=np.float32)
        
        for i in range(n_base):
            vid = base_unity_vids[i]
            
            # 현재 프레임과 이전 프레임 모두에 해당 ID가 있어야 델타 계산 가능
            if vid in curr_map and vid in prev_map:
                curr_vals = curr_map[vid]
                prev_vals = prev_map[vid]
                
                # [누적 델타] 현재 값 - 이전 값 (Previous)
                diff_pos = curr_vals[0] - prev_vals[0]
                diff_rot = curr_vals[1] - prev_vals[1]
                diff_opac = curr_vals[2] - prev_vals[2]

                # ==========================================
                # [좌표계 보정] Z축 반전 (Incremental)
                # ==========================================
                diff_pos[2] *= -1 
                # diff_pos[0] *= -1 # 필요시 X축 반전

                delta_buffer[i, 0:3] = diff_pos
                delta_buffer[i, 3:7] = diff_rot
                delta_buffer[i, 10] = diff_opac

        # 다음 루프를 위해 현재 데이터를 '이전 데이터'로 업데이트
        prev_map = curr_map

        output_name = f"frame_{f_num}.delta"
        delta_buffer.tofile(os.path.join(output_dir, output_name))
        print(f"누적 프레임 {f_num} 생성 완료: {os.path.basename(curr_ply_path)}")

    print("\n[완료] 누적 방식(Incremental) 델타 생성 완료.")

# 실행
generate_incremental_delta(
    asset_dir='C:/Users/jeong/GS_Project/projects/GaussianExample/Assets/GaussianAssets',
    frames_root='C:/4DGS/dynerf_coffee_martini',
    output_dir='C:/Users/jeong/GS_Project/projects/GaussianExample/Assets/DeltaOutput'
)