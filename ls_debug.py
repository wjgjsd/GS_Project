
import os

print(f"CWD: {os.getcwd()}")
files = [f for f in os.listdir('.') if "point_cloud" in f]
print(f"Found {len(files)} files matching 'point_cloud'.")
for f in files[:20]:
    print(f)
