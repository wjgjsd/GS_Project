"""
PLY Normalizer for 4DGS Animation

이 스크립트는 4DGS 애니메이션의 모든 프레임 .ply 파일을 읽어서:
1. 전체 애니메이션의 global bounds를 계산합니다
2. 모든 프레임의위치를 통일된 bounds로 정규화합니다
3. 새로운 .ply 파일로 저장합니다

이렇게 하면 Unity import 시 모든 프레임이 동일한 chunk bounds를 가지게 되어,
같은 ID의 Gaussian이 같은 normalized position을 유지합니다.
"""

import numpy as np
import struct
import os
import glob
from pathlib import Path

def read_ply_header(f):
    """Read PLY header and return attribute list"""
    lines = []
    while True:
        line = f.readline().decode('ascii').strip()
        lines.append(line)
        if line == 'end_header':
            break
    
    vertex_count = int([l for l in lines if 'element vertex' in l][0].split()[2])
    attrs = [(l.split()[2], l.split()[1]) for l in lines if l.startswith('property')]
    
    return lines, vertex_count, attrs

def get_attr_stride(attrs):
    """Calculate byte stride for each vertex"""
    stride = 0
    for _, typ in attrs:
        if typ in ['float', 'int']:
            stride += 4
        elif typ == 'double':
            stride += 8
        elif typ == 'uchar':
            stride += 1
    return stride

def read_ply_file(path):
    """Read entire PLY file and return raw binary data"""
    with open(path, 'rb') as f:
        header_lines, vertex_count, attrs = read_ply_header(f)
        stride = get_attr_stride(attrs)
        data = f.read(vertex_count * stride)
    
    return header_lines, vertex_count, attrs, stride, data

def extract_positions(data, vertex_count, attrs, stride):
    """Extract x, y, z positions from binary data"""
    # Find x, y, z offsets
    offset = 0
    x_offset, y_offset, z_offset = None, None, None
    for name, typ in attrs:
        if name == 'x':
            x_offset = offset
        elif name == 'y':
            y_offset = offset
        elif name == 'z':
            z_offset = offset
        
        if typ in ['float', 'int']:
            offset += 4
        elif typ == 'double':
            offset += 8
        elif typ == 'uchar':
            offset += 1
    
    # Extract positions
    positions = np.zeros((vertex_count, 3), dtype=np.float32)
    for i in range(vertex_count):
        vertex_offset = i * stride
        x = struct.unpack('f', data[vertex_offset + x_offset:vertex_offset + x_offset + 4])[0]
        y = struct.unpack('f', data[vertex_offset + y_offset:vertex_offset + y_offset + 4])[0]
        z = struct.unpack('f', data[vertex_offset + z_offset:vertex_offset + z_offset + 4])[0]
        positions[i] = [x, y, z]
    
    return positions

def compute_global_bounds(input_dir, frame_pattern):
    """Compute global min/max bounds across all frames"""
    print("Computing global bounds...")
    
    global_min = np.array([float('inf'), float('inf'), float('inf')])
    global_max = np.array([float('-inf'), float('-inf'), float('-inf')])
    
    frame_paths = sorted(glob.glob(os.path.join(input_dir, frame_pattern)))
    print(f"Found {len(frame_paths)} frames")
    
    for i, path in enumerate(frame_paths):
        if i % 10 == 0:
            print(f"  Processing frame {i+1}/{len(frame_paths)}...")
        
        _, vertex_count, attrs, stride, data = read_ply_file(path)
        positions = extract_positions(data, vertex_count, attrs, stride)
        
        frame_min = np.min(positions, axis=0)
        frame_max = np.max(positions, axis=0)
        
        global_min = np.minimum(global_min, frame_min)
        global_max = np.maximum(global_max, frame_max)
    
    print(f"\nGlobal Bounds:")
    print(f"  Min: {global_min}")
    print(f"  Max: {global_max}")
    print(f"  Size: {global_max - global_min}")
    
    return global_min, global_max

def normalize_ply_file(input_path, output_path, global_min, global_max):
    """Normalize a single PLY file using global bounds"""
    header_lines, vertex_count, attrs, stride, data = read_ply_file(input_path)
    
    # Find x, y, z offsets
    offset = 0
    x_offset, y_offset, z_offset = None, None, None
    for name, typ in attrs:
        if name == 'x':
            x_offset = offset
        elif name == 'y':
            y_offset = offset
        elif name == 'z':
            z_offset = offset
        
        if typ in ['float', 'int']:
            offset += 4
        elif typ == 'double':
            offset += 8
        elif typ == 'uchar':
            offset += 1
    
    # Modify positions in-place
    data = bytearray(data)
    for i in range(vertex_count):
        vertex_offset = i * stride
        
        # Read
        x = struct.unpack('f', data[vertex_offset + x_offset:vertex_offset + x_offset + 4])[0]
        y = struct.unpack('f', data[vertex_offset + y_offset:vertex_offset + y_offset + 4])[0]
        z = struct.unpack('f', data[vertex_offset + z_offset:vertex_offset + z_offset + 4])[0]
        
        # Normalize
        x_norm = (x - global_min[0]) / (global_max[0] - global_min[0])
        y_norm = (y - global_min[1]) / (global_max[1] - global_min[1])
        z_norm = (z - global_min[2]) / (global_max[2] - global_min[2])
        
        # Write back
        struct.pack_into('f', data, vertex_offset + x_offset, x_norm)
        struct.pack_into('f', data, vertex_offset + y_offset, y_norm)
        struct.pack_into('f', data, vertex_offset + z_offset, z_norm)
    
    # Write output
    with open(output_path, 'wb') as f:
        for line in header_lines:
            f.write((line + '\n').encode('ascii'))
        f.write(bytes(data))

def normalize_all_frames(input_dir, frame_pattern, output_dir):
    """Normalize all frames using global bounds"""
    # Compute global bounds
    global_min, global_max = compute_global_bounds(input_dir, frame_pattern)
    
    # Create output directory
    os.makedirs(output_dir, exist_ok=True)
    
    # Normalize each frame
    frame_paths = sorted(glob.glob(os.path.join(input_dir, frame_pattern)))
    print(f"\nNormalizing {len(frame_paths)} frames...")
    
    for i, input_path in enumerate(frame_paths):
        frame_name = os.path.basename(os.path.dirname(os.path.dirname(input_path)))
        output_path = os.path.join(output_dir, frame_name, 'point_cloud', 'iteration_xxx', 'point_cloud.ply')
        os.makedirs(os.path.dirname(output_path), exist_ok=True)
        
        # Copy iteration folder name from input
        iteration_name = os.path.basename(os.path.dirname(input_path))
        output_path = os.path.join(output_dir, frame_name, 'point_cloud', iteration_name, 'point_cloud.ply')
        os.makedirs(os.path.dirname(output_path), exist_ok=True)
        
        if i % 10 == 0:
            print(f"  Frame {i+1}/{len(frame_paths)}: {frame_name}")
        
        normalize_ply_file(input_path, output_path, global_min, global_max)
    
    print("\nDone! Normalized PLY files saved to:", output_dir)
    print(f"Global bounds stored: Min={global_min}, Max={global_max}")

if __name__ == "__main__":
    import argparse
    
    parser = argparse.ArgumentParser(description='Normalize 4DGS PLY files using global bounds')
    parser.add_argument('--input_dir', required=True, help='Input directory containing frame folders')
    parser.add_argument('--pattern', default='*/point_cloud/iteration_*/point_cloud.ply', help='Glob pattern for PLY files')
    parser.add_argument('--output_dir', required=True, help='Output directory for normalized PLY files')
    
    args = parser.parse_args()
    
    normalize_all_frames(args.input_dir, args.pattern, args.output_dir)
