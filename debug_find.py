
import os

asset_dir = r"C:\Users\jeong\GS_Project\projects\GaussianExample\Assets\GaussianAssets"
frame_idx = 1
target_pattern = f"{frame_idx:04d}-point_cloud"
print(f"Target Pattern: '{target_pattern}'")

if os.path.exists(asset_dir):
    files = os.listdir(asset_dir)
    print(f"Total files: {len(files)}")
    
    found = False
    for f in files:
        if target_pattern in f and f.endswith("_ids.bytes"):
            print(f"MATCH FOUND: {f}")
            found = True
            break
            
    if not found:
        print("No match found.")
        # Print candidates
        candidates = [f for f in files if "0001" in f]
        print(f"Candidates with '0001': {candidates[:5]}")
else:
    print("Asset dir does not exist")
