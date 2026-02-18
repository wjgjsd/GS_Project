
import os
import numpy as np

def analyze_frame(frame_idx, asset_dir):
    target_pattern = f"{frame_idx:04d}-point_cloud"
    files = os.listdir(asset_dir)
    found_prefix = None
    for f in files:
        if target_pattern in f and f.endswith("_ids.bytes"):
            found_prefix = f.replace("_ids.bytes", "")
            break
            
    if not found_prefix:
        print(f"Frame {frame_idx} not found")
        return

    print(f"\n--- Analyzing Frame {frame_idx} ({found_prefix}) ---")
    path_pos = os.path.join(asset_dir, f"{found_prefix}_pos.bytes")
    path_oth = os.path.join(asset_dir, f"{found_prefix}_oth.bytes") # Rot+Scale

    # Pos
    if os.path.exists(path_pos):
        pos = np.fromfile(path_pos, dtype=np.float32).reshape(-1, 3)
        print(f"Pos Range: Min {pos.min(axis=0)}, Max {pos.max(axis=0)}")
        print(f"Pos Mean: {pos.mean(axis=0)}")
        print(f"Pos Max Dist from origin: {np.max(np.linalg.norm(pos, axis=1))}")
    
    # Scale
    if os.path.exists(path_oth):
        oth = np.fromfile(path_oth, dtype=np.uint8)
        n = len(pos)
        stride = len(oth) // n
        # Scale is offset 4, length 12
        scale_bytes = oth[:n*stride].reshape(n, stride)[:, 4:16]
        scale = scale_bytes.flatten().view(np.float32).reshape(n, 3)
        
        print(f"Scale Range: Min {scale.min()}, Max {scale.max()}")
        print(f"Scale Mean: {scale.mean(axis=0)}")
        print(f"Scale > 8.0 count: {np.sum(scale > 8.0)}")
        print(f"Scale > 20.0 count: {np.sum(scale > 20.0)}")

asset_dir = r"C:\Users\jeong\GS_Project\projects\GaussianExample\Assets\GaussianAssets"
analyze_frame(1, asset_dir)
analyze_frame(4, asset_dir)
