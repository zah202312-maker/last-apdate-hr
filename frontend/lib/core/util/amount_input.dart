import 'package:flutter/services.dart';

/// Puts a comma every three digits of the whole part (1250000.5 → 1,250,000.5).
String groupThousands(String plain) {
  final dot = plain.indexOf('.');
  final whole = dot < 0 ? plain : plain.substring(0, dot);
  final fraction = dot < 0 ? '' : plain.substring(dot);
  final out = StringBuffer();
  for (var i = 0; i < whole.length; i++) {
    if (i > 0 && (whole.length - i) % 3 == 0) out.write(',');
    out.write(whole[i]);
  }
  return '$out$fraction';
}

/// The number without its separators, ready for [double.tryParse].
String ungroup(String text) => text.replaceAll(',', '').trim();

/// Formats an amount field as the user types: commas every three digits, an optional point and up to [decimals]
/// digits after it. The cursor stays after the same digit, and backspace on a comma removes the digit before it.
class ThousandsInputFormatter extends TextInputFormatter {
  const ThousandsInputFormatter({this.decimals = 4});

  final int decimals;

  @override
  TextEditingValue formatEditUpdate(TextEditingValue oldValue, TextEditingValue newValue) {
    var text = newValue.text;
    var cursor = newValue.selection.end < 0 ? text.length : newValue.selection.end;

    // Backspace right after a comma: delete the digit before it instead of the comma alone.
    final removedOne = oldValue.text.length == text.length + 1 && oldValue.selection.isCollapsed && cursor < oldValue.text.length;
    if (removedOne && oldValue.text[cursor] == ',' && cursor > 0) {
      text = text.substring(0, cursor - 1) + text.substring(cursor);
      cursor -= 1;
    }

    final plain = ungroup(text);
    if (plain.isEmpty) return const TextEditingValue();
    if (!RegExp('^\\d*\\.?\\d{0,$decimals}\$').hasMatch(plain)) return oldValue;

    final digitsBefore = ungroup(text.substring(0, cursor.clamp(0, text.length))).length;
    final grouped = groupThousands(plain);
    var offset = 0;
    for (var seen = 0; offset < grouped.length && seen < digitsBefore; offset++) {
      if (grouped[offset] != ',') seen++;
    }
    return TextEditingValue(text: grouped, selection: TextSelection.collapsed(offset: offset));
  }
}
