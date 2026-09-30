# FireEscape MR — prototype

Train before the emergency.

## Video
- [Gameplay test v2 (mới nhất)](Docs/Videos/FireEscape_gameplay_test_v2.mp4)
- [Gameplay test v1](Docs/Videos/FireEscape_gameplay_test.mp4)

## Chạy thử trên PC (không cần kính)
1. Mở project bằng Unity 6000.0.78.
2. Chọn menu **FireEscape → 1. Tạo scene PC (bàn phím + chuột)**. Scene được lưu ở `Assets/FireEscape/Scenes/FireEscape_PC.unity`.
3. Nhấn **Play**. Click vào Game view để khoá chuột.

Phím: WASD di chuyển · chuột nhìn · Shift chạy · C/Ctrl cúi · E hoặc chuột trái để tương tác (giữ để phun bình) · F kiểm tra độ nóng của cửa · Q bỏ bình · Esc tạm dừng.

## Chạy trên Quest (MR)
1. Chọn **FireEscape → 2. Tạo scene Quest**. Menu này thêm OVRCameraRig, passthrough, MRUK và bật Scene Support.
2. Trên kính: chạy Space Setup (quét phòng), rồi build Android hoặc chạy qua Quest Link.
3. Nếu quét được phòng: lửa, khói, lối thoát và NPC được đặt ngay trong căn phòng thật. Cửa ra vào thật trở thành lối thoát. Nếu phòng chỉ có một cửa, hệ thống thêm một lối thoát ảo. Nếu không quét được phòng: game chạy chế độ VR trong không gian mô phỏng và dùng joystick để di chuyển.

## Cấu trúc
| Thư mục | Nội dung |
|---|---|
| `Scripts/Core` | Kiểu dữ liệu, nội dung KNOW, các nguyên tắc an toàn cố định (AI chỉ trích dẫn, không tự tạo quy tắc) |
| `Scripts/Simulation` | Không gian, 8 nhóm scenario × 4 level, Incident Engine (lửa, khói, cửa, chặn tuyến, NPC, Time Pressure, Consequence) |
| `Scripts/Analysis` | Ghi hành trình (Replay), chấm điểm, AI Error Analysis, Personal Error Profile, Adaptive AI, What-if |
| `Scripts/Runtime` | Người chơi PC/Quest, dựng môi trường, hiển thị lửa/khói/đèn/NPC, điều phối game |
| `Scripts/UI` | Menu Know/Practice/Mission, HUD, Kết quả, Replay + bản đồ, Hồ sơ |
| `Scripts/MR` | Reality Mapping từ MR Utility Kit |
| `Resources/FireEscapeModels` | 8 model dụng cụ PCCC xuất từ Blender |
| `BlenderSource/` (ngoài Assets) | `fire_equipment.py` và `.blend` gốc |

### NPC hướng dẫn (lính cứu hỏa)
Nguồn: model Tripo AI trong `Downloads\firefighter+3d+model` (turnaround 3 bản, 1,9 triệu tam giác, không có xương).
1. `blender -b -P BlenderSource/firefighter_stageA.py -- <fbx gốc> BlenderSource/firefighter_A.blend <preview_dir>`: tách bản nhìn thẳng, đưa về cao 1,8 m, giảm còn khoảng 40k tam giác.
2. `blender -b BlenderSource/firefighter_A.blend -P BlenderSource/firefighter_remesh.py -- BlenderSource/firefighter_A_remesh.blend`: remesh — voxel 7 mm hàn các mảnh rời của lưới AI thành một khối kín, rồi giảm đều còn 44k tam giác; giữ bản gốc (`FirefighterSource`, ẩn) để bước 4 bake chi tiết sang.
3. `blender -b BlenderSource/firefighter_A_remesh.blend -P BlenderSource/firefighter_stageB.py -- Assets/FireEscape/Resources/FireEscapeModels/Firefighter <preview_dir> <thư mục .fbm>`: tách mặt nạ SCBA (xương `Mask`), vá mặt, dựng 23 xương, gán trọng số, tạo 9 animation và xuất FBX (UV tạm).
4. `blender -b BlenderSource/firefighter_rigged.blend -P BlenderSource/firefighter_stageC_texture.py -- Assets/FireEscape/Resources/FireEscapeModels/Firefighter <preview_dir>`: trải UV mới theo vùng xương, bake màu/normal/roughness/metallic từ bản gốc (texture Tripo 4096) sang lưới mới, làm sạch màu vải và vạch phản quang, xuất lại FBX + texture 2048.
5. Trong Unity: **FireEscape → 4. Tạo NPC hướng dẫn** để tạo material URP, Animator Controller và prefab.

Animation: Idle, Talk, Point, Beckon, Walk, CrouchWalk, MaskOn, MaskOff, MaskOffIdle. NPC xuất hiện ở menu và trong nhiệm vụ Beginner.

Để dựng lại model: `blender -b -P BlenderSource/fire_equipment.py -- Assets/FireEscape/Resources/FireEscapeModels preview.png`

Phần mô phỏng là C# thuần, không phụ thuộc scene, nên What-if chạy lại được cùng tình huống (cùng seed) ở chế độ headless.

Nội dung an toàn cần được đơn vị PCCC rà soát trước khi dùng để huấn luyện thật.
