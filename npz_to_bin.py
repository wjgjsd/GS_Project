import numpy as np

# 1. 파일 로드
data = np.load('params.npz')

# 2. 어떤 데이터가 들어있는지 목록 출력 (이걸 알려주시면 더 정확한 코드를 짤 수 있습니다)
print("내부 파일 목록:", data.files)

# 3. 예시: 위치 데이터와 변화량 데이터 추출 (키값은 데이터셋마다 다를 수 있음)
base_pos = data['xyz']           # 초기 위치 [N, 3]
offsets = data['d_xyz']          # 변화량 [Frames, N, 3]

# 4. 유니티에서 읽기 편하게 바이너리로 저장
base_pos.astype(np.float32).tofile('base_pos.bin')
offsets.astype(np.float32).tofile('offsets.bin')

print(f"추출 완료: 가우시안 {base_pos.shape[0]}개, 총 {offsets.shape[0]}프레임")