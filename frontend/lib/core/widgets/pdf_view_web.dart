import 'dart:js_interop';
import 'dart:typed_data';

import 'package:flutter/widgets.dart';
import 'package:web/web.dart' as web;

/// Web: shows the PDF with the browser's own viewer in an iframe.
Widget? buildPdfView(Uint8List bytes) => _PdfFrame(bytes: bytes);

class _PdfFrame extends StatefulWidget {
  const _PdfFrame({required this.bytes});
  final Uint8List bytes;

  @override
  State<_PdfFrame> createState() => _PdfFrameState();
}

class _PdfFrameState extends State<_PdfFrame> {
  late final String _url = web.URL.createObjectURL(
    web.Blob([widget.bytes.toJS].toJS, web.BlobPropertyBag(type: 'application/pdf')),
  );

  @override
  void dispose() {
    web.URL.revokeObjectURL(_url);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => HtmlElementView.fromTagName(
        tagName: 'iframe',
        onElementCreated: (element) {
          final frame = element as web.HTMLIFrameElement;
          frame.src = _url;
          frame.style
            ..border = 'none'
            ..width = '100%'
            ..height = '100%';
        },
      );
}
