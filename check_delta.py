import numpy as np

file_path = 'projects/GaussianExample/Assets/Deltas_save/frame_0002.delta'
data = np.fromfile(file_path, dtype=np.float32)
data = data.reshape(-1, 3)

norms = np.linalg.norm(data, axis=1)
non_zeros = data[norms > 0]

print(f"Total objects (rows): {len(data)}")
print(f"Non-zero objects: {len(non_zeros)}")

if len(non_zeros) > 0:
    print(f"First non-zero: {non_zeros[0]}")
    print(f"Max abs val in non-zero: {np.abs(non_zeros).max(axis=0)}")
    print(f"Mean abs val in non-zero: {np.abs(non_zeros).mean(axis=0)}")
