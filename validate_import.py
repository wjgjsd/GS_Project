import numpy as np
import os

asset_dir = r'c:\Users\jeong\GS_Project\projects\GaussianExample\Assets\GaussianAssets'

# Test frames
test_frames = [(1, 2), (2, 3), (3, 4), (4, 10), (10, 50)]

print("=" * 60)
print("IMPORT VALIDATION: Frame-to-Frame ID-Matched Delta")
print("=" * 60)

for frame_a, frame_b in test_frames:
    try:
        # Load data
        id_a = np.fromfile(os.path.join(asset_dir, f'coffe_martini_trained-frames-{frame_a:04d}-point_cloud_ids.bytes'), dtype=np.int32)
        id_b = np.fromfile(os.path.join(asset_dir, f'coffe_martini_trained-frames-{frame_b:04d}-point_cloud_ids.bytes'), dtype=np.int32)
        pos_a = np.fromfile(os.path.join(asset_dir, f'coffe_martini_trained-frames-{frame_a:04d}-point_cloud_pos.bytes'), dtype=np.float32).reshape(-1, 3)
        pos_b = np.fromfile(os.path.join(asset_dir, f'coffe_martini_trained-frames-{frame_b:04d}-point_cloud_pos.bytes'), dtype=np.float32).reshape(-1, 3)
        
        # ID matching
        common_ids = np.intersect1d(id_a, id_b)
        sorter_a = np.argsort(id_a)
        sorter_b = np.argsort(id_b)
        idx_a = sorter_a[np.searchsorted(id_a, common_ids, sorter=sorter_a)]
        idx_b = sorter_b[np.searchsorted(id_b, common_ids, sorter=sorter_b)]
        
        # Calculate delta
        delta = pos_b[idx_b] - pos_a[idx_a]
        mean_delta = np.mean(np.linalg.norm(delta, axis=1))
        
        # Status
        status = "[OK]" if mean_delta < 2.0 else "[BAD]"
        
        print(f"\nFrame {frame_a:3d} -> {frame_b:3d}:")
        print(f"  Common IDs: {len(common_ids):6d} / {len(id_a):6d} ({100*len(common_ids)/len(id_a):.1f}%)")
        print(f"  Mean Delta: {mean_delta:8.4f}  {status}")
        
    except Exception as e:
        print(f"\nFrame {frame_a:3d} -> {frame_b:3d}: [ERROR] - {e}")

print("\n" + "=" * 60)
print("Expected: Mean Delta < 2.0 for all frame pairs")
print("If all tests pass, proceed to full delta generation!")
print("=" * 60)
