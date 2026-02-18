
import os
import numpy as np

def inspect_bytes(folder, prefix):
    print(f"\n--- Inspecting: {prefix} ---")
    
    path_ids = os.path.join(folder, f"{prefix}_ids.bytes")
    path_pos = os.path.join(folder, f"{prefix}_pos.bytes")
    
    if not os.path.exists(path_ids):
        print(f"MISSING: {path_ids}")
        return
        
    try:
        # 1. Read IDs
        ids = np.fromfile(path_ids, dtype=np.int32)
        count = len(ids)
        print(f"ID Count: {count}")
        
        if count > 0:
            min_id = ids.min()
            max_id = ids.max()
            unique = len(np.unique(ids))
            print(f"ID Range: [{min_id}, {max_id}]")
            print(f"Unique IDs: {unique}")
            
            if unique != count:
                print("WARNING: DUPLICATE IDs FOUND!")
            else:
                print("IDs are Unique.")
                
            print(f"First 5 IDs: {ids[:5]}")
            
        # 2. Read Pos (Optional, just to check count matches)
        if os.path.exists(path_pos):
            pos = np.fromfile(path_pos, dtype=np.float32)
            pos_count = len(pos) // 3
            print(f"Pos Count: {pos_count}")
            
            if pos_count != count:
                 print(f"WARNING: POS COUNT ({pos_count}) != ID COUNT ({count})")
        
    except Exception as e:
        print(f"Error reading bytes: {e}")

# Path from generate_delta.py
asset_dir = r"C:\Users\jeong\GS_Project\projects\GaussianExample\Assets\GaussianAssets"

if __name__ == "__main__":
    if os.path.exists(asset_dir):
        # Find unique prefixes
        files = [f for f in os.listdir(asset_dir) if f.endswith('_ids.bytes')]
        prefixes = sorted(list(set([f.replace('_ids.bytes', '') for f in files])))[:5] # Check first 5
        
        if not prefixes:
             print(f"No _ids.bytes files found in {asset_dir}")
        else:
             print(f"Found {len(prefixes)} datasets (checking first 5)...")
             for p in prefixes:
                 inspect_bytes(asset_dir, p)
    else:
         print(f"Asset dir not found: {asset_dir}")
