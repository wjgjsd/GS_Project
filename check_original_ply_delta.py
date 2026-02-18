import struct
import numpy as np

def read_ply_positions_and_ids(path):
    """Read all positions and vertex_ids from PLY file"""
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

print("Reading original PLY files...")
print()

# Read Frame 2 and 4
pos2, ids2 = read_ply_positions_and_ids(r'C:\4DGS\dynerf_coffee_martini\0002\point_cloud\iteration_157\point_cloud.ply')
pos4, ids4 = read_ply_positions_and_ids(r'C:\4DGS\dynerf_coffee_martini\0004\point_cloud\iteration_154\point_cloud.ply')

print(f"Frame 2: {len(pos2)} vertices")
print(f"Frame 4: {len(pos4)} vertices")
print()

# Find common IDs
common_ids = np.intersect1d(ids2, ids4)
print(f"Common IDs: {len(common_ids)} ({100*len(common_ids)/len(ids2):.1f}%)")
print()

# Match by ID
sorter2 = np.argsort(ids2)
sorter4 = np.argsort(ids4)
idx2 = sorter2[np.searchsorted(ids2, common_ids, sorter=sorter2)]
idx4 = sorter4[np.searchsorted(ids4, common_ids, sorter=sorter4)]

# Calculate delta
delta = pos4[idx4] - pos2[idx2]
delta_norms = np.linalg.norm(delta, axis=1)
mean_delta = np.mean(delta_norms)

print(f"Original PLY Frame 2->4 (ID-matched):")
print(f"  Mean Delta: {mean_delta:.4f}")
print(f"  Max Delta:  {np.max(delta_norms):.4f}")
print(f"  Min Delta:  {np.min(delta_norms):.4f}")
print()

# Check a few specific IDs
print("First 5 matched IDs:")
for i in range(5):
    test_id = common_ids[i]
    idx_2 = idx2[i]
    idx_4 = idx4[i]
    d = delta_norms[i]
    print(f"  ID {test_id:6d}: Delta = {d:8.4f}")
