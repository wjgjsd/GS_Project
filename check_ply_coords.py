import struct
import numpy as np

def read_ply_first_positions(path, n=10):
    """Read first n vertex positions from PLY file"""
    with open(path, 'rb') as f:
        # Read header
        lines = []
        while True:
            line = f.readline().decode('ascii').strip()
            lines.append(line)
            if line == 'end_header':
                break
        
        # Parse header
        vertex_count = int([l for l in lines if  'element vertex' in l][0].split()[2])
        attrs = [(l.split()[2], l.split()[1]) for l in lines if l.startswith('property')]
        
        # Calculate stride
        stride = sum(4 if t in ['float', 'int'] else 8 if t == 'double' else 1 for _, t in attrs)
        
        # Read first n vertices
        data = f.read(min(n, vertex_count) * stride)
        vertices = []
        for i in range(min(n, vertex_count)):
            offset = i * stride
            x, y, z = struct.unpack('fff', data[offset:offset+12])
            vertices.append([x, y, z])
        
        return np.array(vertices)

# Read first positions from original PLY files
p1 = read_ply_first_positions(r'C:\4DGS\dynerf_coffee_martini\0001\point_cloud\iteration_8000\point_cloud.ply')
p2 = read_ply_first_positions(r'C:\4DGS\dynerf_coffee_martini\0002\point_cloud\iteration_157\point_cloud.ply')
p4 = read_ply_first_positions(r'C:\4DGS\dynerf_coffee_martini\0004\point_cloud\iteration_154\point_cloud.ply')

print('Original PLY Frame 1 first 5:')
print(p1[:5])
print('\nOriginal PLY Frame 2 first 5:')
print(p2[:5])
print('\nOriginal PLY Frame 4 first 5:')
print(p4[:5])

print('\nDelta F2-F1 (first 5):', np.linalg.norm(p2[:5] - p1[:5], axis=1))
print('Delta F4-F1 (first 5):', np.linalg.norm(p4[:5] - p1[:5], axis=1))

print('\nMean positions:')
print('Frame 1:', np.mean(p1, axis=0))
print('Frame 2:', np.mean(p2, axis=0))
print('Frame 4:', np.mean(p4, axis=0))
