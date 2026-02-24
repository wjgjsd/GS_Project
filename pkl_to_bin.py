import torch
import numpy as np

def export_to_bin(pkl_path):
    print(f"로드 중: {pkl_path}")
    # map_location을 lambda로 주면 CUDA 드라이버 없이도 무조건 읽습니다.
    data = torch.load(pkl_path, map_location=lambda storage, loc: storage, weights_only=False)
    latents = data.get('latents', data)

    # 1. 좌표 (xyz) 추출 및 Z축 반전
    xyz = latents['xyz'].numpy().astype(np.float32)
    # 2. 움직임 정보 (flow) 추출 및 Z축 반전
    flow = latents['flow'].numpy().astype(np.float32)
    
    # 유니티 좌표계 대응 (오른손 -> 왼손)
    xyz[:, 2] *= -1
    flow[:, 2] *= -1

    # 바이너리 저장 (N * 3 * float32)
    xyz.tofile("queen_pos.bin")
    flow.tofile("queen_flow.bin")
    
    print(f"추출 완료: 총 {len(xyz)}개의 가우시안 데이터가 .bin으로 저장되었습니다.")

if __name__ == "__main__":
    export_to_bin('point_cloud.pkl')