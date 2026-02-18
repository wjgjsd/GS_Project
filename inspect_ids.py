
import numpy as np
import os
import sys

# Correct path as per User Instruction
BASE_DIR = r"c:\Users\jeong\GS_Project\projects\GaussianExample\Assets\GaussianAssets"

def load_data(frame_idx, base_dir="."):
    # Use BASE_DIR if default "." fails (or always use it)
    if base_dir == ".":
        base_dir = BASE_DIR
    
    # Flexible loading: Scan for file containing '{frame_idx:04d}' and ends with '_ids.bytes'
    if not os.path.exists(base_dir):
        print(f"[ERROR] Dir not found: {base_dir}")
        return None, None
        
    candidates = [f for f in os.listdir(base_dir) if f"{frame_idx:04d}" in f and f.endswith("_ids.bytes")]
    
    if not candidates:
        print(f"[ERROR] No file found for frame {frame_idx:04d} in {base_dir}")
        return None, None
        
    # Pick first match (assuming unique frame ID in filename)
    filename = candidates[0]
    prefix = filename.replace("_ids.bytes", "")
    
    path_ids = os.path.join(base_dir, f"{prefix}_ids.bytes")
    path_pos = os.path.join(base_dir, f"{prefix}_pos.bytes")
    
    
    # Debug info
    if not os.path.exists(path_ids):
        print(f"CWD: {os.getcwd()}")
        print(f"Files in CWD: {os.listdir(base_dir)[:5]}")
        
        print(f"[ERROR] File not found: {path_ids}")
        # Try full path if needed
        full_path = f"c:\\Users\\jeong\\GS_Project\\{prefix}_ids.bytes"
        if os.path.exists(full_path):
            path_ids = full_path
            path_pos = f"c:\\Users\\jeong\\GS_Project\\{prefix}_pos.bytes"
        else:
            return None, None
            
    try:
        ids = np.fromfile(path_ids, dtype=np.int32)
        pos = np.fromfile(path_pos, dtype=np.float32).reshape(-1, 3)
        return ids, pos
    except Exception as e:
        print(f"[ERROR] Failed to load {path_ids}: {e}")
        return None, None

def inspect_ids(f1, f2):
    print(f"Loading Frame {f1}...")
    ids1, pos1 = load_data(f1)
    print(f"Loading Frame {f2}...")
    ids2, pos2 = load_data(f2)
    
    if ids1 is None or ids2 is None:
        print("Failed to load data.")
        return

    print(f"Frame {f1} Count: {len(ids1)}")
    print(f"Frame {f2} Count: {len(ids2)}")
    
    # Check Sequentiality
    is_seq1 = np.all(ids1 == np.arange(len(ids1)))
    print(f"Frame {f1} IDs Sequential (0,1,2...)? {is_seq1}")
    
    common_ids = np.intersect1d(ids1, ids2)
    print(f"Common IDs: {len(common_ids)}")
    
    if len(common_ids) == 0:
        print("NO COMMON IDS! CRITICAL FAILURE.")
        return

    # Check Delta Magnitude for Common IDs
    # Pick random samples
    if len(common_ids) > 10:
        samples = np.random.choice(common_ids, 10, replace=False)
    else:
        samples = common_ids
        
    print(f"\nInspecting {len(samples)} Random Common IDs:")
    
    max_dist = 0
    total_dist = 0
    
    for id_val in samples:
        idx1 = np.where(ids1 == id_val)[0][0]
        idx2 = np.where(ids2 == id_val)[0][0]
        
        p1 = pos1[idx1]
        p2 = pos2[idx2]
        dist = np.linalg.norm(p2 - p1)
        
        max_dist = max(max_dist, dist)
        total_dist += dist
        
        print(f"ID {id_val}: Dist {dist:.4f}")
        print(f"  P1: {p1}")
        print(f"  P2: {p2}")
        
    avg_dist = total_dist / len(samples)
    print(f"\nAverage Distance: {avg_dist:.4f}")
    print(f"Max Distance: {max_dist:.4f}")
    
    if avg_dist > 1.0:
        print("\n[CONCLUSION] HUGE DELTAS DETECTED. IDs are likely spatially inconsistent.")
    elif avg_dist < 0.0001:
        print("\n[CONCLUSION] ZERO MOVEMENT. Something is frozen?")
    else:
        print("\n[CONCLUSION] Reasonable movement. IDs look consistent.")

if __name__ == "__main__":
    inspect_ids(1, 2)
