import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/generated/hr_api.dart';
import '../../core/l10n/strings.dart';
import '../../core/network/api_providers.dart';
import '../../core/widgets/common.dart';
import 'ops_shared.dart';

// Store development (decision 2026-11-11): labels and pickers shared by the store screens.

String stockStatusLabel(S s, StockStatus st) => switch (st) {
      StockStatus.inStock => s.t('متوفر', 'In stock'),
      StockStatus.lowStock => s.t('منخفض المخزون', 'Low stock'),
      StockStatus.outOfStock => s.t('نفد المخزون', 'Out of stock'),
      _ => '—',
    };

Tone stockStatusTone(StockStatus st) => switch (st) {
      StockStatus.inStock => Tone.success,
      StockStatus.lowStock => Tone.warning,
      _ => Tone.danger,
    };

String documentStatusLabel(S s, StoreDocumentStatus st) => switch (st) {
      StoreDocumentStatus.draft => s.t('مسودة', 'Draft'),
      StoreDocumentStatus.approved => s.t('معتمد', 'Approved'),
      StoreDocumentStatus.reversed => s.t('معكوس', 'Reversed'),
      _ => '—',
    };

Tone documentStatusTone(StoreDocumentStatus st) => switch (st) {
      StoreDocumentStatus.approved => Tone.success,
      StoreDocumentStatus.reversed => Tone.neutral,
      _ => Tone.info,
    };

/// Money with its currency, two decimals at most.
String money(num? v, String currency) => v == null ? '—' : '${qty(v)} $currency';

String dateText(DateTime d) => '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

Future<List<ItemDto>> searchStoreItems(WidgetRef ref, String q) async =>
    (await ref.read(hrApiProvider).store.Item_GetList(filter: q.isEmpty ? null : q, isActive: true, maxResultCount: 50)).items;

/// The next serial after the highest one in use: PREFIX-00001, PREFIX-00002, ...
@visibleForTesting
String nextSerial(String prefix, Iterable<String> used) {
  final pattern = RegExp('^${RegExp.escape(prefix)}' r'(\d+)$');
  var last = 0;
  for (final code in used) {
    final n = int.tryParse(pattern.firstMatch(code)?.group(1) ?? '');
    if (n != null && n > last) last = n;
  }
  return '$prefix${(last + 1).toString().padLeft(5, '0')}';
}

/// Suggests the next serial item code (ITM-00001, ...). Returns null when the lookup fails, so the officer types it.
Future<String?> nextItemSku(WidgetRef ref) async {
  try {
    final r = await ref.read(hrApiProvider).store.Item_GetList(filter: 'ITM-', sorting: 'sku desc', maxResultCount: 200);
    return nextSerial('ITM-', r.items.map((i) => i.sku));
  } catch (_) {
    return null;
  }
}

/// Suggests the next serial issue voucher number (ISS-00001, ...). Returns null when the lookup fails.
Future<String?> nextVoucherNumber(WidgetRef ref) async {
  try {
    final r = await ref.read(hrApiProvider).store.StoreIssueVoucher_GetList(filter: 'ISS-', sorting: 'voucherNumber desc', maxResultCount: 200);
    return nextSerial('ISS-', r.items.map((v) => v.voucherNumber));
  } catch (_) {
    return null;
  }
}

Future<List<ItemCategoryDto>> searchCategories(WidgetRef ref, String q) async =>
    (await ref.read(hrApiProvider).store.ItemCategory_GetList(filter: q.isEmpty ? null : q, isActive: true, maxResultCount: 100)).items;

Future<List<ItemUnitDto>> searchUnits(WidgetRef ref, String q) async =>
    (await ref.read(hrApiProvider).store.ItemUnit_GetList(filter: q.isEmpty ? null : q, isActive: true, maxResultCount: 100)).items;

Future<List<StoreTeamDto>> searchTeams(WidgetRef ref, String q) async =>
    (await ref.read(hrApiProvider).store.StoreTeam_GetList(filter: q.isEmpty ? null : q, isActive: true, maxResultCount: 100)).items;

String itemName(BuildContext context, ItemDto i) => catalogName(context, i.nameAr, i.nameEn);

/// English names are optional (decision 2026-11-16): without one, the Arabic name shows in English too.
String catalogName(BuildContext context, String ar, String? en) =>
    Localizations.localeOf(context).languageCode == 'ar' || en == null || en.trim().isEmpty ? ar : en;

/// A simple bordered table for document lines and report rows; scrolls sideways on phones.
class StoreTable extends StatelessWidget {
  const StoreTable({super.key, required this.columns, required this.rows, this.footer});
  final List<String> columns;
  final List<List<Widget>> rows;
  final List<Widget>? footer;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    DataCell cell(Widget w) => DataCell(w);
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: DataTable(
        headingTextStyle: theme.textTheme.labelLarge?.copyWith(fontWeight: FontWeight.w700),
        columnSpacing: 20,
        columns: [for (final c in columns) DataColumn(label: Text(c))],
        rows: [
          for (final r in rows) DataRow(cells: r.map(cell).toList()),
          if (footer != null) DataRow(color: WidgetStatePropertyAll(theme.colorScheme.surfaceContainerHighest), cells: footer!.map(cell).toList()),
        ],
      ),
    );
  }
}
