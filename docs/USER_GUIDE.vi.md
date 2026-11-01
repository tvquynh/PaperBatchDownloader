# Hướng dẫn sử dụng Paper Batch Downloader V5.3.2

## Tải bổ sung giữa PTIT/IP và VNU trong V5.3.2

Chọn cùng một CSV và một thư mục PDF cho cả hai chế độ. Tải bằng PTIT/IP trước; sau đó chờ PDF đang truyền xong, chọn VNU và bấm **Đổi hồ sơ**. Ứng dụng tự nạp lại danh sách, giữ các bài Completed có PDF hợp lệ và đưa các bài còn thiếu về Pending. Chuẩn bị đăng nhập VNU trong LIBRARY ACCESS nếu cần, rồi bấm **START**. Khi chuyển về PTIT/IP, thao tác tương tự: các bài vừa tải bằng VNU cũng được giữ lại.

Không cần tạo CSV mới, không cần đổi thư mục và không cần Load CSV lại ngay sau khi đổi hồ sơ. Cookie và đăng nhập vẫn riêng. Không tự bắt đầu tải trước khi bạn bấm START.

Tiến độ dùng chung được lưu tại `%LOCALAPPDATA%\PaperBatchDownloader\SharedQueues\<batch>`. Đường dẫn CSV, nội dung CSV và thư mục PDF tạo thành một bộ tiến độ riêng. Nếu đóng rồi mở lại, chọn lại cùng CSV và thư mục, bấm Load CSV để khôi phục. PDF bị xóa hoặc không còn hoàn chỉnh sẽ được xếp tải lại. Báo cáo lượt chạy phân biệt bài đã có trước và bài hoàn thành trong lượt này.

Các PDF cũ được khôi phục từ trạng thái đã lưu nếu DOI, thư mục, tên tệp và dấu hiệu PDF hoàn chỉnh khớp. Điều này áp dụng cả tên save_as/file_name và tên do nhà xuất bản đặt. Chỉ có một tệp cùng tên mà không có trạng thái khớp chưa đủ để kết luận đúng bài; ứng dụng không tự ghi đè tệp có sẵn. Ứng dụng giữ lại dữ liệu cũ, không tự chuyển PDF sang thư mục khác.


## V5.3.2: chọn quyền truy cập và tải

1. Chọn **PTIT/IP — mạng hiện tại** nếu dùng mạng có quyền truy cập. Ứng dụng mở trực tiếp nhà xuất bản, không tự đổi IP hoặc VPN.
2. Chọn **VNU — OpenAthens**, bấm **Đổi hồ sơ**, rồi vào **LIBRARY ACCESS** để đăng nhập các nguồn cần dùng. Bài hỗ trợ sẽ đi qua cổng thư viện. Bạn tự hoàn tất đăng nhập hoặc CAPTCHA khi nguồn yêu cầu.
3. Hai chế độ có cookie và log riêng; hàng đợi và tiến độ dùng chung cho cùng CSV và thư mục PDF. Bản này không nhập hồ sơ cũ V5.2.11; hãy đăng nhập lại lần đầu. Không mở hai cửa sổ cùng hồ sơ.
4. **PDF đồng thời** mặc định 2 (1–4); **PDF/nguồn** mặc định 1 (1–2). Browser có thể mở bài tiếp trong khi PDF trước đang truyền. Tải HTTP trực tiếp trong chế độ IP vẫn tuần tự; tốc độ phụ thuộc nguồn và mạng.
5. **Đăng xuất VNU** dừng hàng đợi, chờ truyền xong, yêu cầu đăng xuất OpenAthens và xóa cookie/dữ liệu trang/cache của hồ sơ VNU. PDF và hàng đợi được giữ lại. Việc này không đăng xuất trình duyệt khác hoặc bảo đảm chấm dứt mọi phiên phía máy chủ nhà xuất bản. Nếu báo lỗi, đóng và mở lại ứng dụng trước khi dùng tiếp.
6. Khi gặp trang ScienceDirect có thông báo lỗi kèm mã **CPE00002**, nguồn bị tạm ngưng trong lượt hiện tại; các bài chưa chạy cùng nguồn được Deferred, nguồn khác tiếp tục. Chỉ thử lại sau khi nguồn hoạt động bình thường.
7. Khung browser mặc định cao hơn; kéo thanh ngăn để điều chỉnh. Bộ lọc cột chỉ đổi các hàng hiển thị, không giới hạn hàng đợi tải.

**Log để so sánh hai chế độ:** dùng cùng CSV, thư mục PDF trống riêng và cùng giới hạn tải. Sau mỗi lượt, bấm **Export result CSV** và **OPEN LOG FOLDER**. Mỗi lượt có `run_id`, dấu SHA-256 CSV, `events.jsonl` và `run-summary-<run_id>.json`. Kiểm tra, che thông tin riêng trước khi chia sẻ. Dữ liệu nằm dưới `%LOCALAPPDATA%\PaperBatchDownloader\Profiles\PTIT_IP` hoặc `VNU_ACCOUNT`.


## 1. Cài đặt và mở ứng dụng

Ứng dụng tải PDF từ danh sách CSV bằng trình duyệt WebView2, sử dụng quyền truy cập mà bạn có. Giao diện điều khiển chính dùng tiếng Anh; phần truy cập thư viện có nhãn tiếng Việt.

1. Mở [Releases](https://github.com/tvquynh/PaperBatchDownloader/releases/latest), tải **PaperBatchDownloader-v5.3.2-win-x64.zip**. Không chọn “Source code” nếu chỉ muốn chạy chương trình.
2. Giải nén toàn bộ ZIP, chẳng hạn vào `Documents\PaperBatchDownloader`. Không chạy trực tiếp trong cửa sổ ZIP.
3. Mở `PaperBatchDownloader-v5.3.2.exe` bằng tài khoản Windows thông thường. Không cần chạy quyền quản trị cho hoạt động tải thông thường.
4. Chờ thông báo **Browser ready**.
5. Nếu báo thiếu WebView2, cài **Evergreen Runtime** từ [Microsoft](https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section), rồi mở lại ứng dụng. Bản ZIP đã chứa .NET runtime nên không cần .NET SDK để chạy.

Release cung cấp bản Windows x64. Cần kết nối Internet và thư mục kết quả có quyền ghi. Ứng dụng chưa có bản macOS/Linux hoặc ARM64 native. Dung lượng và thời gian phụ thuộc số lượng, kích thước bài và phản hồi nhà xuất bản; nên thử một nhóm nhỏ trước.

## 2. Lượt tải đầu tiên

1. **CSV…** → chọn `examples/quickstart_oa.csv` trong thư mục đã giải nén.
2. **Load CSV** để nạp danh sách vào bảng. Kiểm tra Title, DOI và Selected URL.
3. **Folder…** → chọn một thư mục mới, ví dụ `Documents\PDF_Quickstart`.
4. Nếu cần đăng nhập thư viện, làm theo mục 4 trước khi tải.
5. **START** để bắt đầu. Không đóng ứng dụng khi PDF còn đang truyền.
6. Khi hàng đợi kết thúc, kiểm tra các dòng **Completed** và mở thử PDF. **Export result CSV** lưu báo cáo toàn bộ danh sách.

Để thử phạm vi rộng hơn, dùng `new_oa_test/NEW_OA_150_3_each.csv` với một thư mục kết quả mới. Bộ này có 150 bài, 50 tạp chí/hội nghị, mỗi nơi 3 bài. OA là thông tin từ metadata/giấy phép hoặc kỷ yếu chính thức, không phải bảo đảm mọi URL luôn tải được. Các bài được chọn cho kiểm thử tải; không phải tất cả đều nghiên cứu mã độc.

<a id="csv"></a>
## 3. Chuẩn bị CSV

CSV dùng dấu phẩy, dòng đầu là tên cột. Nên lưu **CSV UTF-8**. Trong Excel, kiểm tra tệp xuất thật sự dùng dấu phẩy; tệp phân cách bằng dấu chấm phẩy cần xuất lại. Nếu tiêu đề chứa dấu phẩy hoặc xuống dòng, đặt trường trong dấu ngoặc kép và viết dấu ngoặc kép bên trong thành hai dấu ngoặc kép.

### Danh sách URL đơn giản

```csv
url
https://arxiv.org/pdf/1706.03762
```

### Danh sách DOI và tên tệp

```csv
doi,title,save_as
10.48550/arXiv.1706.03762,Attention Is All You Need,attention.pdf
```

### Các cột quan trọng

| Cột | Ý nghĩa |
| --- | --- |
| `doi` hoặc `doi_url` | DOI thuần hoặc liên kết DOI; dùng khi chưa có URL ưu tiên khác. |
| `best_oa_pdf_url` | URL PDF ưu tiên cao nhất. |
| `pdf_url` hoặc `oa_url` | URL OA/PDF tiếp theo; nếu cùng có, `pdf_url` được ưu tiên khi nhập. |
| `best_oa_landing_page` hoặc `landing_page` | Trang bài báo; ưu tiên trường đầu nếu cả hai cùng có. |
| `url` | URL dự phòng, hoặc cột duy nhất cho danh sách đơn giản. |
| `title` | Tên bài để hiển thị và lọc. |
| `save_as` | Tên tệp cố định, duy nhất cho mỗi dòng, chỉ tên `.pdf`, không chứa đường dẫn thư mục. |
| `publisher`, `source` | Thông tin nhà xuất bản/tạp chí giúp phân nhóm. |
| `is_open_access`, `oa_status` | Metadata mô tả; không tự cấp quyền truy cập. |
| `record_id`, `openalex_id`, `publication_year` | Thông tin định danh/năm tùy chọn, có thể xuất trong báo cáo. |

Thứ tự chọn URL: **best_oa_pdf_url → pdf_url/oa_url → best_oa_landing_page/landing_page → DOI → url**. Một URL không truy cập được ở cột ưu tiên cao không có nghĩa ứng dụng sẽ tự thay bằng mọi cột còn lại. Hãy sửa dữ liệu đầu vào nếu Selected URL không phù hợp.

Nếu khai báo `save_as`, chương trình giữ đúng tên đó và không tự ghi đè tệp đã tồn tại. Khi để trống, tên lấy từ phản hồi/trang tải hoặc tên dự phòng. Không đưa cùng bài nhiều lần trừ khi đang cố ý thử nhiều đường truy cập và có tên đầu ra riêng.

## 4. Đăng nhập thư viện trước khi tải

1. Nạp CSV trước để ứng dụng biết mỗi nguồn có bao nhiêu bài.
2. Bấm **LIBRARY ACCESS** hoặc thẻ **Truy cập thư viện**.
3. Chọn nguồn và bấm **Đăng nhập nguồn**. Đăng nhập, MFA hoặc CAPTCHA trực tiếp trong trình duyệt bên dưới.
4. Nếu có cửa sổ SSO phụ, hoàn tất tại cửa sổ đó. Nó dùng chung hồ sơ WebView với cửa sổ chính.
5. **Nguồn tiếp theo** chuyển lần lượt qua danh sách. Có thể bỏ qua nguồn không cần dùng; không bắt buộc đăng nhập đủ mọi nguồn mới cho START.
6. Với nguồn có bài trong CSV, bấm **Mở bài mẫu**, chờ trang hiện đầy đủ, rồi **Kiểm tra trang**. Nếu có bài không được metadata đánh dấu OA, ứng dụng ưu tiên bài đó để kiểm tra.
7. Sau khi chuẩn bị xong, chọn thư mục kết quả và bấm **START** cho các dòng Pending, hoặc **RETRY INCOMPLETE** để chạy tiếp các dòng chưa hoàn tất.

Ứng dụng có sẵn 14 đường vào theo danh sách VNU: arXiv, BNEUF, IEEE Xplore, MathSciNet, MIT OpenCourseWare, Nature, Open Dissertations, Ovid, Oxford Scholarship Online, ProQuest, ScienceDirect, Scopus, SpringerLink và SpringerOpen. Nguồn tra cứu/học liệu không mặc nhiên là nguồn PDF bài báo. Các nguồn khác trong CSV, ví dụ ACM, có thể hiện đường vào trực tiếp với ghi chú chưa có cổng VNU.

**Nếu bạn không thuộc VNU:** chọn nguồn, thay **URL đăng nhập** bằng đường dẫn thư viện/tổ chức của bạn, bấm **Lưu URL**, rồi **Đăng nhập nguồn**. Không sử dụng tài khoản VNU nếu bạn không có quyền. Việc lưu URL không lưu một bản xác nhận đã đăng nhập; thông tin kiểm tra được đặt lại ở phiên ứng dụng mới.

OpenAthens có thể mở trang đăng nhập tổ chức trước khi tới nhà xuất bản; đây là bước bình thường. Nếu phiên SSO vẫn còn hiệu lực, có thể tới thẳng nguồn mà không thấy trang đăng nhập. Đăng nhập trong Chrome/Edge riêng không tự chuyển cookie sang ứng dụng. Phiên có thể hết hạn theo chính sách của nguồn.

Các kết quả kiểm tra có ý nghĩa giới hạn: **Cần đăng nhập**, **Cần xử lý CAPTCHA**, **Bài mẫu yêu cầu mua/thuê bao**, **Bài mẫu có liên kết PDF; chưa xác minh tải**, hoặc **Trang xác nhận quyền bài mẫu; chưa tải thử PDF**. Kết quả cho một bài không chứng minh quyền với tất cả bài khác. Có URL PDF chưa bảo đảm phản hồi là PDF hợp lệ.

Kéo thanh ngăn giữa bảng và trình duyệt để tăng diện tích trang đăng nhập nếu cần. Không đổi sang nguồn đăng nhập khi PDF đang tải/khởi tạo tải; hãy chờ lượt đó hoàn tất.

## 5. Các nút và thiết lập

| Điều khiển | Chức năng |
| --- | --- |
| **Load CSV** | Nạp CSV và thử khôi phục trạng thái của đúng đường dẫn CSV đã lưu gần nhất. Nạp CSV cũng xóa bộ lọc bảng. |
| **START** | Chạy các dòng Pending. Không tự chạy lại toàn bộ dòng Completed. |
| **PAUSE** | Dừng tiến hàng đợi, giữ trình duyệt và phiên đăng nhập. Tải native đã bắt đầu có thể vẫn tiếp tục; không coi PAUSE là hủy tải. |
| **CONTINUE CURRENT** | Phân tích lại bài hiện tại sau khi bạn xử lý trang trong luồng tải. Sau chế độ LIBRARY ACCESS, dùng START hoặc RETRY INCOMPLETE. |
| **SKIP** | Bỏ qua thủ công bài hiện tại và chuyển bài tiếp theo nếu đang chạy. |
| **RETRY INCOMPLETE** | Đặt lại các dòng có thể thử lại và chạy tiếp; giữ Completed. PDF còn tải nền được tránh chạy trùng. |
| **RETRY PUBLISHER** | Thử lại các dòng chưa hoàn tất của nhà xuất bản thuộc dòng đang chọn. |
| **OPEN PUBLISHER LOGIN** | Mở cổng đăng nhập của nhà xuất bản thuộc dòng đang chọn, dùng đường thư viện đã cấu hình nếu có. |
| **Export result CSV** | Xuất báo cáo toàn bộ hàng đợi, kể cả các dòng bị bộ lọc ẩn. |
| **Preflight/publisher** | Số bài đầu của mỗi nhà xuất bản được xếp chạy thử trước; không phải xác nhận quyền hay số lượt tải song song. |
| **Wait timeout (s)** | Thời gian chờ chú ý/tải theo cơ chế hiện có, 15–600 giây. Tiến độ tải native gia hạn thời gian chờ không hoạt động. |
| **OPEN LOG FOLDER** | Mở thư mục chẩn đoán của phiên hiện tại. |
| **SAVE WEBVIEW SNAPSHOT** | Lưu nội dung trang đang thấy và bằng chứng URL/PDF để phân tích. Nên Pause trước khi chụp một lỗi. |

Hàng đợi thử trước vài bài mỗi nhà xuất bản rồi xen kẽ phần còn lại. Sau timeout đăng nhập/CAPTCHA, ứng dụng có thể hoãn các dòng còn lại của cùng nhà xuất bản để tránh lặp vô ích. Sau khi đăng nhập, dùng RETRY PUBLISHER hoặc RETRY INCOMPLETE.

## 6. Trạng thái và paywall

| Trạng thái | Cách hiểu / xử lý |
| --- | --- |
| Pending | Chưa chạy; dùng START. |
| Navigating / LookingForPdf | Đang mở trang hoặc tìm PDF. |
| Downloading | Đang khởi tạo hoặc nhận PDF. |
| Completed | Ứng dụng đã ghi nhận tệp qua kiểm tra PDF hiện có. Nên mở mẫu để xác nhận nội dung bài. |
| LoginRequired / InstitutionalAccessRequired | Cần đăng nhập hoặc xác thực tổ chức trong WebView. |
| BotCheck | Cần bạn xử lý CAPTCHA/kiểm tra người dùng. |
| WaitingForUser / ManualAttention | Xem trang, Decision và Error để xác định điều cần làm. |
| Deferred | Hoãn để thử lại sau; đọc lý do, không đồng nghĩa mọi bài đó đều đã tải lỗi riêng lẻ. |
| Failed / NoUrl | Lỗi được ghi nhận hoặc thiếu URL hợp lệ; kiểm tra CSV/trang nguồn. |
| Skipped | Đã bỏ qua, gồm URL không phải bài báo như robots.txt. |

Paywall rõ ràng hoặc thông báo tổ chức không đăng ký bài sẽ được ghi **Deferred – Paywall** và chuyển ngay, không chờ hết timeout. Login/CAPTCHA và thông báo access denied chưa rõ giữ xử lý riêng. Sau khi quyền truy cập thay đổi, bạn có thể thử lại các bài Deferred. Ứng dụng không vượt paywall hay giải CAPTCHA.

## 7. Lọc, tìm và sao chép

Bấm tiêu đề cột **Title ▾**, DOI, Status, Publisher… để mở bộ lọc. Nhập chữ và Enter/**Apply filter**; so khớp không phân biệt hoa thường. Status, Publisher và URL source còn có lựa chọn giá trị chính xác. Nhiều cột kết hợp theo điều kiện cùng thỏa mãn. Dấu chấm ở tiêu đề cho biết bộ lọc đang bật.

**Clear this column** xóa một cột; **Clear all filters** hiện lại toàn bộ. Thanh lọc hiển thị số dòng đang thấy/tổng số dòng. Bộ lọc chỉ đổi bảng hiển thị, không thu hẹp START, retry, lưu trạng thái hoặc xuất kết quả.

Chuột phải vào ô để copy ô đó, DOI, DOI URL, Selected URL, PDF URL, Title, File path hoặc Error/Decision. Trong WebView có mục **Copy current page URL**.

## 8. Chạy tiếp và nâng cấp

Đóng bản cũ khi không còn lượt truyền PDF đang hoạt động, giải nén bản mới vào thư mục riêng rồi mở EXE mới. Hồ sơ ứng dụng nằm ở LocalAppData nên việc đổi thư mục EXE không chủ động xóa cookie.

Để khôi phục lượt gần nhất: giữ nguyên **đường dẫn tuyệt đối của CSV**, thứ tự/dữ liệu bài và thư mục PDF; Load CSV đó, chọn lại đúng thư mục kết quả, rồi RETRY INCOMPLETE. Bản sao CSV ở vị trí khác được coi là hàng đợi khác. V5.3.2 lưu tiến độ theo đường dẫn/nội dung CSV và thư mục PDF, dùng chung giữa PTIT/IP và VNU.

Nếu muốn một lượt hoàn toàn mới, sao chép CSV sang đường dẫn mới và chọn một thư mục kết quả trống. Không xóa PDF cũ chỉ để tránh lỗi trùng tên; di chuyển/đối chiếu chúng trước. Một số file hoàn tất muộn được khôi phục sau kiểm tra định danh và dấu kết thúc PDF. Các file Springer cũ được nhận diện là phụ lục/tài liệu tham khảo sẽ được hoãn; khi thử lại đúng thư mục, file đó được giữ riêng trong Diagnostics trước khi tải bài chính.

## 9. Chẩn đoán và lỗi thường gặp

| Hiện tượng | Kiểm tra |
| --- | --- |
| Không mở được trình duyệt | Cài/cập nhật WebView2 Evergreen từ Microsoft rồi mở lại ứng dụng. |
| No URL / dữ liệu vào một cột | Header CSV, dấu phẩy và UTF-8; kiểm tra cột URL được hỗ trợ. |
| “File already exists” | `save_as` trùng hoặc thư mục có file cũ; xác minh/di chuyển file, hoặc dùng thư mục mới. |
| Đã đăng nhập nhưng vẫn yêu cầu mua | Đăng nhập tài khoản cá nhân chưa chắc kích hoạt quyền tổ chức; kiểm tra đúng cổng thư viện, gói thuê bao và bài cụ thể. |
| Nguồn đăng nhập bị hoãn hàng loạt | Hoàn tất xác thực trong LIBRARY ACCESS, sau đó RETRY PUBLISHER. |
| PDF tải lâu | Kiểm tra mạng/kích thước và log tiến độ; tải nền có thể hoàn tất sau khi hàng đợi chuyển bài. Không bấm thử lại liên tục. |
| Downloaded file is not a valid PDF | Phản hồi có thể là HTML đăng nhập/challenge; mở trang, xử lý quyền và thử lại. |
| Nhiều Deferred trong bộ OA | OA metadata có thể cũ/sai hoặc là bản ở kho khác; đọc trang và lý do cụ thể. |

Khi báo lỗi, cung cấp phiên bản, Windows/WebView2, một DOI/URL công khai, ảnh lỗi và đoạn log đã che dữ liệu riêng. Không công bố cả hồ sơ WebView. [Quyền riêng tư](PRIVACY.md) mô tả vị trí và nội dung dữ liệu cục bộ.

## 10. Giới hạn

Không bảo đảm tương thích mọi nhà xuất bản hoặc mọi kiểu SSO, quyền thuê bao hay tốc độ tải. Trang nguồn có thể thay đổi và yêu cầu cập nhật bộ phát hiện. Dấu `%PDF-` và các kiểm tra hiện tại không thay thế xác nhận ngữ nghĩa từng trang của bài. Các kho mẫu là đầu vào kiểm thử độ phủ, không phải chứng nhận chất lượng nội dung hoặc xếp hạng tạp chí. Hãy tuân thủ quyền truy cập và điều kiện sử dụng của nguồn.
