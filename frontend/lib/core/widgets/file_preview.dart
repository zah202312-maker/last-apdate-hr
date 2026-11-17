import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/generated/hr_api.dart';
import '../../api/runtime/api_client.dart';
import '../l10n/strings.dart';
import '../theme/tokens.dart';
import '../util/download.dart';
import 'pdf_view.dart';

/// Opens an attachment inside the app: images (zoomable) everywhere, PDFs where the platform can show them.
/// Any other type, or a PDF the platform cannot show, offers the download instead.
Future<void> previewFile(
  BuildContext context, {
  required String fileName,
  required Future<HrBinaryResponse> Function() load,
}) {
  return Navigator.of(context).push(MaterialPageRoute<void>(
    fullscreenDialog: true,
    builder: (_) => FilePreviewPage(fileName: fileName, load: load),
  ));
}

/// Previews a pre-signed link (FileDownloadUrlDto). The link is fetched without the Authorization header,
/// like the download it replaces: the token in the link is the credential.
Future<void> previewLink(BuildContext context, Future<FileDownloadUrlDto> Function() getLink, {required String fileName}) {
  return previewFile(context, fileName: fileName, load: () async {
    final link = await getLink();
    final r = await Dio().get<List<int>>(link.url, options: Options(responseType: ResponseType.bytes));
    return HrBinaryResponse(
      bytes: Uint8List.fromList(r.data ?? const []),
      fileName: fileName,
      contentType: r.headers.value(Headers.contentTypeHeader),
    );
  });
}

enum _Kind { image, pdf, other }

_Kind _kindOf(String fileName, String? contentType) {
  final type = (contentType ?? '').toLowerCase();
  final name = fileName.toLowerCase();
  if (type.startsWith('image/') || RegExp(r'\.(png|jpe?g|gif|webp|bmp)$').hasMatch(name)) return _Kind.image;
  if (type.contains('pdf') || name.endsWith('.pdf')) return _Kind.pdf;
  return _Kind.other;
}

class FilePreviewPage extends StatefulWidget {
  const FilePreviewPage({super.key, required this.fileName, required this.load});
  final String fileName;
  final Future<HrBinaryResponse> Function() load;

  @override
  State<FilePreviewPage> createState() => _FilePreviewPageState();
}

class _FilePreviewPageState extends State<FilePreviewPage> {
  late Future<HrBinaryResponse> _file = widget.load();

  @override
  Widget build(BuildContext context) {
    final s = S.of(context);
    return Scaffold(
      appBar: AppBar(
        title: Text(widget.fileName, overflow: TextOverflow.ellipsis),
        actions: [
          FutureBuilder<HrBinaryResponse>(
            future: _file,
            builder: (context, snap) => IconButton(
              tooltip: s.t('تنزيل', 'Download'),
              icon: const Icon(Icons.download),
              onPressed: snap.hasData ? () => saveBinary(snap.data!, fallbackName: widget.fileName) : null,
            ),
          ),
        ],
      ),
      body: FutureBuilder<HrBinaryResponse>(
        future: _file,
        builder: (context, snap) {
          if (snap.connectionState != ConnectionState.done) return const Center(child: CircularProgressIndicator());
          if (snap.hasError) {
            return _Message(
              icon: Icons.error_outline,
              text: s.error(snap.error!),
              action: TextButton(onPressed: () => setState(() => _file = widget.load()), child: Text(s.t('إعادة المحاولة', 'Retry'))),
            );
          }
          final file = snap.data!;
          switch (_kindOf(widget.fileName, file.contentType)) {
            case _Kind.image:
              return InteractiveViewer(
                maxScale: 6,
                child: Center(
                  child: Image.memory(
                    file.bytes,
                    errorBuilder: (_, __, ___) => _cannotPreview(s, file),
                  ),
                ),
              );
            case _Kind.pdf:
              return buildPdfView(file.bytes) ?? _cannotPreview(s, file);
            case _Kind.other:
              return _cannotPreview(s, file);
          }
        },
      ),
    );
  }

  Widget _cannotPreview(S s, HrBinaryResponse file) => _Message(
        icon: Icons.insert_drive_file_outlined,
        text: s.t('لا يمكن معاينة هذا النوع من الملفات داخل التطبيق', 'This file type cannot be previewed in the app'),
        action: FilledButton.icon(
          onPressed: () => saveBinary(file, fallbackName: widget.fileName),
          icon: const Icon(Icons.download),
          label: Text(s.t('تنزيل', 'Download')),
        ),
      );
}

class _Message extends StatelessWidget {
  const _Message({required this.icon, required this.text, this.action});
  final IconData icon;
  final String text;
  final Widget? action;

  @override
  Widget build(BuildContext context) => Center(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(mainAxisSize: MainAxisSize.min, children: [
            Icon(icon, size: 48, color: context.hr.textSecondary),
            const SizedBox(height: 12),
            Text(text, textAlign: TextAlign.center),
            if (action != null) ...[const SizedBox(height: 16), action!],
          ]),
        ),
      );
}
