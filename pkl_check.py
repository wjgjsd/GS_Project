import pickle
import io

# torch.load의 내부 동작을 흉내내는 가짜 클래스들입니다.
# 이를 통해 torch 라이브러리 없이도 파일을 열 수 있습니다.
class FakeUnpickler(pickle.Unpickler):
    def find_class(self, module, name):
        if module.startswith('torch'):
            return MagicMock
        try:
            return super().find_class(module, name)
        except:
            return MagicMock

class MagicMock:
    def __init__(self, *args, **kwargs): self.args = args; self.kwargs = kwargs
    def __setstate__(self, state): self.state = state
    def __call__(self, *args, **kwargs): return self
    @classmethod
    def _load_from_bytes(cls, b): return "RawData"

def final_inspect(file_path):
    print(f"--- [최종 병기] torch 없이 로드 시도: {file_path} ---")
    try:
        with open(file_path, 'rb') as f:
            # torch.load가 내부적으로 사용하는 pickle.Unpickler를 커스텀 버전으로 대체
            unpickler = FakeUnpickler(f)
            data = unpickler.load()

        latents = data.get('latents', data)
        print("\n[성공] 데이터 키 목록:")
        for k, v in latents.items():
            # v가 우리가 만든 MagicMock 객체인 경우 내부의 shape 정보를 찾아냅니다.
            shape = "알 수 없음"
            if hasattr(v, 'args') and len(v.args) > 2:
                shape = v.args[2] # 보통 3번째 인자가 shape입니다.
            elif isinstance(v, tuple):
                shape = v
            
            print(f" > 키: {k:10} | 예상 형상(Shape): {shape}")

    except Exception as e:
        print(f"\n[실패] 이 방법으로도 안 된다면 파일 자체가 손상되었을 확률이 높습니다: {e}")

if __name__ == "__main__":
    final_inspect('point_cloud.pkl')