// In-app PDF view.
// Web: the browser's own PDF viewer inside an iframe.
// Mobile/desktop: none yet (needs a PDF package), so the caller offers the download.
export 'pdf_view_io.dart' if (dart.library.js_interop) 'pdf_view_web.dart';
