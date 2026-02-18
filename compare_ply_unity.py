import numpy as np
import struct

def read_ply_with_ids(path):
    """Read PLY file and return positions and vertex_ids"""
    with open(path, 'rb') as f:
        # Read header
        lines = []
        while True:
            line = f.readline().decode('ascii').strip()
            lines.append(line)
            if line == 'end_header':
                break
        
        # Parse header
        vertex_count = int([l for l in lines if 'element vertex' in l][0].split()[2])
        attrs = [(l.split()[2], l.split()[1]) for l in lines if l.startswith('property')]
        
        # Calculate stride
        stride = sum(4 if t in ['float', 'int'] else 8 if t == 'double' else 1 for _, t in attrs)
        
        # Read all data
        data = f.read(vertex_count * stride)
        
        # Extract positions and IDs
        positions = []
        ids = []
        for i in range(vertex_count):
            offset = i * stride
            # x, y, z are first 3 floats
            x, y, z = struct.unpack('fff', data[offset:offset+12])
            # vertex_id is last int (4 bytes before end of vertex)
            vertex_id = struct.unpack('i', data[offset + stride - 4:offset + stride])[0]
            positions.append([x, y, z])
            ids.append(vertex_id)
        
        return np.array(positions), np.array(ids)

# Read original PLY
print("Reading original PLY...")
ply_pos, ply_ids = read_ply_with_ids(r'C:\4DGS\dynerf_coffee_martini\0001\point_cloud\iteration_8000\point_cloud.ply')

# Read Unity import
print("Reading Unity import...")
unity_pos = np.fromfile(r'c:\Users\jeong\GS_Project\projects\GaussianExample\Assets\GaussianAssets\coffe_martini_trained-frames-0001-point_cloud_pos.bytes', dtype=np.float32).reshape(-1, 3)
unity_ids = np.fromfile(r'c:\Users\jeong\GS_Project\projects\GaussianExample\Assets\GaussianAssets\coffe_martini_trained-frames-0001-point_cloud_ids.bytes', dtype=np.int32)

print(f"\nPLY: {len(ply_pos)} vertices")
print(f"Unity: {len(unity_pos)} vertices")

print("\nPLY first 5 IDs:", ply_ids[:5])
print("Unity first 5 IDs:", unity_ids[:5])

# Check if same ID has same position
print("\n=== ID Matching Check ===")
for i in range(5):
    test_id = ply_ids[i]
    ply_idx = i
    unity_where = np.where(unity_ids == test_id)[0]
    
    if len(unity_where) > 0:
        unity_idx = unity_where[0]
        print(f"\nID {test_id}:")
        print(f"  PLY pos  [idx={ply_idx}]: {ply_pos[ply_idx]}")
        print(f"  Unity pos[idx={unity_idx}]: {unity_pos[unity_idx]}")
        print(f"  MATCH: {np.allclose(ply_pos[ply_idx], unity_pos[unity_idx])}")
    else:
        print(f"\nID {test_id}: NOT FOUND IN UNITY!")
