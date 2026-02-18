import numpy as np
import os
import glob
import argparse

def load_asset_data(f_num, asset_dir, preferred_prefix=""):
    """Load position, rotation, scale, opacity, and IDs for a given frame."""
    
    # Try to find files with glob
    pos_pattern = os.path.join(asset_dir, f"{preferred_prefix}*{f_num:04d}*-point_cloud_pos.bytes")
    ids_pattern = os.path.join(asset_dir, f"{preferred_prefix}*{f_num:04d}*-point_cloud_ids.bytes")
    rot_pattern = os.path.join(asset_dir, f"{preferred_prefix}*{f_num:04d}*-point_cloud_rot.bytes")
    scl_pattern = os.path.join(asset_dir, f"{preferred_prefix}*{f_num:04d}*-point_cloud_scale.bytes")
    col_pattern = os.path.join(asset_dir, f"{preferred_prefix}*{f_num:04d}*-point_cloud_col.bytes")
    
    pos_files = glob.glob(pos_pattern)
    ids_files = glob.glob(ids_pattern)
    rot_files = glob.glob(rot_pattern)
    scl_files = glob.glob(scl_pattern)
    col_files = glob.glob(col_pattern)
    
    if not pos_files or not ids_files:
        return None
    
    path_pos = pos_files[0]
    path_ids = ids_files[0]
    path_rot = rot_files[0] if rot_files else None
    path_scl = scl_files[0] if scl_files else None
    path_col = col_files[0] if col_files else None
    
    # Extract prefix from filename
    filename = os.path.basename(path_pos)
    base_prefix_full = filename.replace("-point_cloud_pos.bytes", "")
    
    # Load IDs
    ids = np.fromfile(path_ids, dtype=np.int32)
    n_points = len(ids)
    
    # Load Position
    size_pos = os.path.getsize(path_pos)
    expected_size = n_points * 12  # 3 floats * 4 bytes
    if size_pos != expected_size:
        print(f"  [ERROR] Frame {f_num} ({base_prefix_full}): Pos file size {size_pos} != Expected {expected_size}. Corrupt!")
        return None
    
    pos = np.fromfile(path_pos, dtype=np.float32).reshape(-1, 3)
    
    # Load Rotation (Norm8x4 or Float32x4)
    if path_rot:
        size_rot = os.path.getsize(path_rot)
        if size_rot == n_points * 4:  # Norm8x4
            rot_packed = np.fromfile(path_rot, dtype=np.uint32)
            # Unpack (simplified, assuming XYZW order)
            rot = np.zeros((n_points, 4), dtype=np.float32)
            rot[:, 0] = ((rot_packed >> 0) & 0xFF) / 255.0
            rot[:, 1] = ((rot_packed >> 8) & 0xFF) / 255.0
            rot[:, 2] = ((rot_packed >> 16) & 0xFF) / 255.0
            rot[:, 3] = ((rot_packed >> 24) & 0xFF) / 255.0
            rot = rot * 2.0 - 1.0
        else:
            rot = np.fromfile(path_rot, dtype=np.float32).reshape(-1, 4)
    else:
        rot = np.zeros((n_points, 4), dtype=np.float32)
        rot[:, 3] = 1.0  # Identity quaternion
    
    # Load Scale
    if path_scl:
        scale = np.fromfile(path_scl, dtype=np.float32).reshape(-1, 3)
    else:
        scale = np.ones((n_points, 3), dtype=np.float32)
    
    # Load Opacity
    if path_col:
        size_col = os.path.getsize(path_col)
        if size_col == n_points * 4:  # Norm8x4
            col_data = np.fromfile(path_col, dtype=np.uint8).reshape(-1, 4)
            opac = col_data[:, 3].astype(np.float32) / 255.0
        elif size_col == n_points * 16:  # Float32x4
            col_data = np.fromfile(path_col, dtype=np.float32).reshape(-1, 4)
            opac = col_data[:, 3]
        else:
            opac = np.ones((n_points,), dtype=np.float32)
    else:
        opac = np.ones((n_points,), dtype=np.float32)
    
    return ids, pos, rot, scale, opac, base_prefix_full


def process_deltas_frame_to_frame(frames, asset_dir, output_dir):
    """
    V27.27 - Frame-to-Frame ID Matching
    Compare each frame with the PREVIOUS frame (not base frame)
    Handles variable point counts by mapping deltas back to base frame size
    """
    ATTENUATION = 0.05
    
    print("Loading Base Frame (1)...")
    base_data = load_asset_data(1, asset_dir)
    if not base_data:
        print("[ERROR] Cannot load base frame!")
        return
    
    b_ids, b_pos, b_rot, b_scale, b_opac, base_filename = base_data
    n_base = len(b_ids)
    
    # Extract prefix: "4DGS-dynerf_coffee_martini-0001" -> "4DGS-dynerf_coffee_martini"
    if "-0001" in base_filename:
        preferred_prefix = base_filename.split("-0001")[0]
    else:
        preferred_prefix = ""
    
    print(f"Base Filename: '{base_filename}' -> Prefix: '{preferred_prefix}'")
    print(f"Base Points: {n_base}")
    
    # Track previous frame for frame-to-frame comparison
    # Start with base frame
    prev_ids = b_ids.copy()
    prev_pos = b_pos.copy()
    prev_rot = b_rot.copy()
    prev_scale = b_scale.copy()
    prev_opac = b_opac.copy()
    
    os.makedirs(output_dir, exist_ok=True)
    
    for f_num in range(1, frames + 1):
        print(f"Processing Frame {f_num}...")
        
        # Always output base frame size
        d_pos_base = np.zeros((n_base, 3), dtype=np.float32)
        d_rot_base = np.zeros((n_base, 4), dtype=np.float32)
        d_scl_base = np.zeros((n_base, 3), dtype=np.float32)
        d_opac_base = np.zeros((n_base,), dtype=np.float32)
        
        if f_num == 1:
            # Frame 1: Zero delta (base frame)
            print(f"  Frame 1: Base Frame (Zero Delta)")
        else:
            # Load current frame
            curr_data = load_asset_data(f_num, asset_dir, preferred_prefix=preferred_prefix)
            
            if not curr_data:
                print(f"  [WARNING] Frame {f_num} missing. Using Zero Delta (frame frozen).")
                # Don't update prev_* so next frame compares to last valid frame
            else:
                c_ids, c_pos, c_rot, c_scale, c_opac, _ = curr_data
                n_curr = len(c_ids)
                n_prev = len(prev_ids)
                
                # Calculate delta in Prev→Curr space
                d_pos_prev = np.zeros((n_prev, 3), dtype=np.float32)
                d_rot_prev = np.zeros((n_prev, 4), dtype=np.float32)
                d_scl_prev = np.zeros((n_prev, 3), dtype=np.float32)
                d_opac_prev = np.zeros((n_prev,), dtype=np.float32)
                
                # ID Matching between PREV and CURR
                sorter = np.argsort(c_ids)
                insert_indices = np.searchsorted(c_ids, prev_ids, sorter=sorter)
                insert_indices = np.clip(insert_indices, 0, len(c_ids) -1)
                c_indices_mapped = sorter[insert_indices]
                matched_mask = (c_ids[c_indices_mapped] == prev_ids)
                
                match_count = np.sum(matched_mask)
                match_pct = (match_count / n_prev) * 100.0
                
                # Calculate delta for matched IDs
                if match_count > 0:
                    d_pos_prev[matched_mask] = c_pos[c_indices_mapped[matched_mask]] - prev_pos[matched_mask]
                    d_rot_prev[matched_mask] = c_rot[c_indices_mapped[matched_mask]] - prev_rot[matched_mask]
                    d_scl_prev[matched_mask] = c_scale[c_indices_mapped[matched_mask]] - prev_scale[matched_mask]
                    d_opac_prev[matched_mask] = c_opac[c_indices_mapped[matched_mask]] - prev_opac[matched_mask]
                    
                    mean_delta = np.mean(np.linalg.norm(d_pos_prev[matched_mask], axis=1))
                    print(f"  Frame {f_num}: Prev({n_prev}) → Curr({n_curr}), ID Match {match_count}/{n_prev} ({match_pct:.1f}%), Mean Delta = {mean_delta:.4f}")
                else:
                    print(f"  Frame {f_num}: No ID matches!")
                
                # Map delta from Prev space to Base space
                sorter_prev = np.argsort(prev_ids)
                insert_indices_base = np.searchsorted(prev_ids, b_ids, sorter=sorter_prev)
                insert_indices_base = np.clip(insert_indices_base, 0, len(prev_ids) - 1)
                prev_indices_mapped = sorter_prev[insert_indices_base]
                base_matched_mask = (prev_ids[prev_indices_mapped] == b_ids)
                
                # Copy deltas for matched base IDs
                d_pos_base[base_matched_mask] = d_pos_prev[prev_indices_mapped[base_matched_mask]]
                d_rot_base[base_matched_mask] = d_rot_prev[prev_indices_mapped[base_matched_mask]]
                d_scl_base[base_matched_mask] = d_scl_prev[prev_indices_mapped[base_matched_mask]]
                d_opac_base[base_matched_mask] = d_opac_prev[prev_indices_mapped[base_matched_mask]]
                
                # Update prev to current (for next iteration)
                prev_ids = c_ids.copy()
                prev_pos = c_pos.copy()
                prev_rot = c_rot.copy()
                prev_scale = c_scale.copy()
                prev_opac = c_opac.copy()
        
        # Apply attenuation and clipping
        d_pos_base *= ATTENUATION
        d_pos_base = np.clip(d_pos_base, -5.0, 5.0)
        
        # Quaternion flip check (simplified)
        d_rot_base *= ATTENUATION
        d_scl_base *= ATTENUATION
        d_opac_base *= ATTENUATION
        
        # Pack into structured array (48 bytes per particle)
        dtype_delta = np.dtype([
            ('pos', np.float32, 3),    # 12 bytes
            ('rot', np.float32, 4),    # 16 bytes
            ('scale', np.float32, 3),  # 12 bytes
            ('opac', np.float32),      # 4 bytes
            ('pad', np.float32)        # 4 bytes
        ])
        
        arr = np.zeros(n_base, dtype=dtype_delta)
        arr['pos'] = d_pos_base
        arr['rot'] = d_rot_base
        arr['scale'] = d_scl_base
        arr['opac'] = d_opac_base
        
        # Write to file
        path_delta = os.path.join(output_dir, f"frame_{f_num:04d}.delta")
        with open(path_delta, "wb") as f:
            f.write(arr.tobytes())
    
    print(f"Processing Complete. {frames} frames written to {output_dir}")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--frames", type=int, default=300)
    parser.add_argument("--asset_dir", type=str, default=".")
    parser.add_argument("--output_dir", type=str, default="deltas")
    args = parser.parse_args()
    
    process_deltas_frame_to_frame(args.frames, args.asset_dir, args.output_dir)