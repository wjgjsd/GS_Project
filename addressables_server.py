#!/usr/bin/env python3
"""
Unity Addressables HTTP Server - 네트워크 전송량 측정용

번들 파일을 HTTP로 서빙하고 실시간 전송량을 콘솔에 출력합니다.

사용법:
1. Unity에서 Addressables 빌드 (Tools > Gaussian > Fix Bundle Mode > 캐시 삭제 + 재빌드)
2. 이 스크립트 실행: python addressables_server.py
3. Unity Play Mode = Use Existing Build 로 설정 후 Play
"""

import http.server
import socketserver
import os
import sys
import time
import threading

PORT = 8000
# ServerData 폴더 전체를 루트로 서빙 (StandaloneWindows64 포함)
DIRECTORY = "projects/GaussianExample/ServerData"

# 전송량 추적
stats = {
    "total_bytes": 0,
    "session_bytes": 0,
    "request_count": 0,
    "bundle_count": 0,
    "start_time": time.time(),
    "last_print_time": time.time(),
}
stats_lock = threading.Lock()


def format_bytes(b):
    if b < 1024:
        return f"{b} B"
    elif b < 1024 ** 2:
        return f"{b/1024:.1f} KB"
    elif b < 1024 ** 3:
        return f"{b/1024**2:.1f} MB"
    else:
        return f"{b/1024**3:.2f} GB"


class TrackingHandler(http.server.SimpleHTTPRequestHandler):
    """전송량 추적 + CORS 지원 HTTP 핸들러"""
    
    protocol_version = "HTTP/1.1"  # Keep-Alive 활성화

    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=DIRECTORY, **kwargs)

    def end_headers(self):
        self.send_header('Access-Control-Allow-Origin', '*')
        self.send_header('Access-Control-Allow-Methods', 'GET, OPTIONS')
        self.send_header('Access-Control-Allow-Headers', 'Content-Type')
        super().end_headers()

    def do_OPTIONS(self):
        self.send_response(200)
        self.end_headers()

    def do_GET(self):
        # 파일 크기 미리 측정
        file_path = self.translate_path(self.path)
        file_size = os.path.getsize(file_path) if os.path.isfile(file_path) else 0

        # 기본 GET 처리
        super().do_GET()

        # 전송량 기록
        if file_size > 0:
            is_bundle = self.path.endswith('.bundle')
            with stats_lock:
                stats["total_bytes"] += file_size
                stats["session_bytes"] += file_size
                stats["request_count"] += 1
                if is_bundle:
                    stats["bundle_count"] += 1

            elapsed = time.time() - stats["start_time"]
            speed = file_size / max(elapsed, 0.001)

            # 번들 요청만 상세 출력
            if is_bundle:
                filename = os.path.basename(self.path)
                print(f"  📦 {filename[:20]}...  "
                      f"{format_bytes(file_size):>10}  "
                      f"누적: {format_bytes(stats['session_bytes']):>10}  "
                      f"속도: {format_bytes(int(speed))}/s")

    def log_message(self, format, *args):
        # 번들 요청이 아니면 로그 억제 (catalog.json 등)
        msg = format % args
        if '.bundle' in msg or 'catalog' in msg.lower():
            pass  # do_GET에서 직접 출력
        # 에러는 항상 출력
        elif '404' in msg or '500' in msg:
            print(f"  ❌ {msg}")


def print_stats_periodically():
    """5초마다 누적 통계 출력"""
    while True:
        time.sleep(5)
        with stats_lock:
            elapsed = time.time() - stats["start_time"]
            avg_speed = stats["session_bytes"] / max(elapsed, 1)
            print(f"\n  ━━━ 통계 ({elapsed:.0f}초 경과) ━━━━━━━━━━━━━━━━━━━━━━━━━━━━")
            print(f"  번들 요청:    {stats['bundle_count']}개")
            print(f"  세션 전송량:  {format_bytes(stats['session_bytes'])}")
            print(f"  평균 속도:    {format_bytes(int(avg_speed))}/s")
            print(f"  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n")


def main():
    # ServerData 폴더 확인
    if not os.path.exists(DIRECTORY):
        print(f"❌ '{DIRECTORY}' 폴더가 없습니다!")
        print(f"\nUnity에서 먼저 빌드하세요:")
        print("  Tools > Gaussian > Fix Bundle Mode > 캐시 삭제 + 재빌드")
        sys.exit(1)

    # StandaloneWindows64 폴더 확인
    win_dir = os.path.join(DIRECTORY, "StandaloneWindows64")
    bundle_count = 0
    if os.path.exists(win_dir):
        bundle_count = len([f for f in os.listdir(win_dir) if f.endswith('.bundle')])

    print("=" * 60)
    print("  Unity Addressables HTTP Server")
    print("=" * 60)
    print(f"  📁 서빙 경로: {os.path.abspath(DIRECTORY)}")
    print(f"  🌐 서버 주소: http://localhost:{PORT}")
    print(f"  📦 번들 파일: {bundle_count}개 발견")
    print()
    print(f"  Unity Remote.LoadPath 설정:")
    print(f"  → http://localhost:{PORT}/StandaloneWindows64")
    print()
    print("  Ctrl+C 로 종료")
    print("=" * 60)
    print()
    print(f"  {'파일명':<25} {'크기':>10}  {'누적':>10}  {'속도':>12}")
    print(f"  {'-'*25} {'-'*10}  {'-'*10}  {'-'*12}")

    # 통계 출력 스레드 시작
    t = threading.Thread(target=print_stats_periodically, daemon=True)
    t.start()

    try:
        socketserver.TCPServer.allow_reuse_address = True
        with socketserver.ThreadingTCPServer(("", PORT), TrackingHandler) as httpd:
            httpd.serve_forever()
    except KeyboardInterrupt:
        print(f"\n\n  🛑 서버 종료")
        print(f"  최종 전송량: {format_bytes(stats['session_bytes'])}")
        print(f"  총 번들 요청: {stats['bundle_count']}개")
    except OSError as e:
        if "Address already in use" in str(e) or "10048" in str(e):
            print(f"\n❌ 포트 {PORT}가 이미 사용 중입니다!")
            print(f"   다른 서버를 종료하거나 스크립트의 PORT를 변경하세요.")
        else:
            print(f"\n❌ 오류: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()
