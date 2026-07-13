# TRENDGOVERNOR LIVE CORE V2 — BUILD 12 CLEAN TERMINAL

## Mục tiêu

Hoàn thiện baseline WPF Trading Terminal theo thiết kế đã chốt, dùng một State Store và một chuỗi nghiệp vụ duy nhất:

`PLAN → VERIFY → EXECUTE → FILL → PROTECT → MANAGE`

## Phạm vi hoàn thành

### Terminal và workspace
- Header trạng thái Binance/User Stream/AUTO.
- 6 KPI đọc từ runtime thật.
- Menu trái: Tổng quan, Radar, Biểu đồ, Kế hoạch, Vị thế, Rủi ro, Hiệu suất, Cảnh báo, API & AUTO LIVE, Nhật ký.
- Radar 40 cặp, phân tích sâu 24 cặp.
- Biểu đồ nến Binance 1H, EMA20/EMA50, Entry/SL/TP.
- Decision và Frozen Entry/SL/TP Plan.
- Vị thế BOT/mở tay và lệnh thường/algo cùng Truth Store.
- Equity/Risk/Alerts/Recent Trades/Logs dùng dữ liệu runtime thật.

### Logic AUTO LIVE
- Một nút BẮT ĐẦU AUTO LIVE.
- Trend dài 1D/4H/1H; timing 15m/5m.
- Large Wave Engine, stability nhiều chu kỳ, không đuổi giá.
- Funding Intelligence chiếm 10% ranking; funding không đảo ngược trend.
- Symbol rules: tickSize, stepSize, minQty, minNotional.
- One-way, ISOLATED, leverage giới hạn.
- FILL xác nhận bằng User Stream/REST reconciliation.
- SL/TP qua conditional Algo Order và xác minh openAlgoOrders.
- Thiếu hard SL trên vị thế BOT: emergency reduce-only close và khóa lệnh mới.
- Position Manager dùng Net PnL gồm PnL giá và funding.

### Dọn source
- Xóa đường gửi lệnh/protection cũ khỏi BinanceClient.
- Chỉ BinanceExecutionGateway được phép EXECUTE/FILL/PROTECT.
- Xóa compatibility aliases không còn dùng.
- Xóa credential vault dư thừa; chỉ giữ một cơ chế lưu API bằng biến môi trường Windows User.
- Không AutoGenerateColumns; bảng có cột và theme cố định.

## Quy tắc dữ liệu
- Không mock Equity, PnL, Funding, Positions hoặc Orders.
- Chưa có dữ liệu thật phải hiển thị rõ trạng thái chưa có dữ liệu.
- Vị thế mở tay không bị Emergency Stop tự động đóng.

## Kiểm tra release bắt buộc
- Restore.
- Build Release win-x64.
- Publish self-contained single EXE.
- Verify EXE.
- Startup smoke test 25 giây.
- Crash-log gate.
- Package integrity và SHA-256.

## Giới hạn xác minh
CI không có API tài khoản nên không thực hiện giao dịch vốn thật. Đường LIVE được build và kiểm tra khởi động; giao dịch thật chỉ được xác minh khi chạy trên máy người dùng với API Futures hợp lệ.
