import 'dart:typed_data';

import 'package:flutter/widgets.dart';

/// Mobile/desktop: no built-in PDF renderer, so null (the preview offers the download).
Widget? buildPdfView(Uint8List bytes) => null;
