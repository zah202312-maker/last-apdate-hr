import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:uuid/uuid.dart';

import '../../api/generated/hr_api.dart';
import '../../core/l10n/strings.dart';
import '../../core/network/api_providers.dart';
import '../../core/session/session_controller.dart';
import '../../core/theme/tokens.dart';
import '../../core/util/format.dart';
import '../../core/widgets/common.dart';
import '../../core/widgets/paged_list.dart';
import '../../core/widgets/pickers.dart';
import '../../core/widgets/file_preview.dart';
import '../ops/ops_shared.dart';
import '../time/time_shared.dart' show pickEmployee;

/// The four levels in order of weight (لفت النظر والثانية بقرار زينب 2026-11-17).
const warningLevels = <WarningLevel>[WarningLevel.notice, WarningLevel.first, WarningLevel.second, WarningLevel.final_];

String warningLevelLabel(S s, WarningLevel? l) => switch (l) {
      WarningLevel.notice => s.t('لفت نظر', 'Notice'),
      WarningLevel.first => s.t('إنذار أول', 'First warning'),
      WarningLevel.second => s.t('إنذار ثانٍ', 'Second warning'),
      WarningLevel.final_ => s.t('إنذار نهائي', 'Final warning'),
      _ => '—',
    };

/// The level picker shared by the recommendation, the HR warning and the decision.
Widget warningLevelField(BuildContext context, WarningLevel value, ValueChanged<WarningLevel> onChanged) {
  final s = S.of(context);
  return DropdownButtonFormField<WarningLevel>(
    initialValue: warningLevels.contains(value) ? value : WarningLevel.first,
    decoration: InputDecoration(labelText: s.t('نوع الإنذار', 'Warning level')),
    items: [for (final l in warningLevels) DropdownMenuItem(value: l, child: Text(warningLevelLabel(s, l)))],
    onChanged: (v) => onChanged(v ?? WarningLevel.first),
  );
}

/// «إرسال إنذار» from HR (Warning_CreateByHr, decision 2026-11-17). With [employee] the recipient is fixed (employee
/// file). The employee is told at once and has 3 working days to reply before HR decides. Returns the warning id, or null.
Future<String?> showHrWarningDialog(BuildContext context, WidgetRef ref, {EmployeeRefDto? employee}) async {
  final s = S.of(context);
  final f = Fmt(context);
  final fixed = employee;
  var level = WarningLevel.first;
  DateTime? date;
  final violationType = TextEditingController();
  final description = TextEditingController();
  final note = TextEditingController();
  final key = const Uuid().v4();
  String? createdId;
  await showFormDialog(
    context,
    width: 620,
    title: s.t('إرسال إنذار', 'Send a warning'),
    submitLabel: s.t('إرسال', 'Send'),
    fields: (setState) => [
      Text(
        s.t('يُبلَّغ الموظف فورًا، وله الرد خلال 3 أيام دوام، ثم تصدر الموارد البشرية القرار.',
            'The employee is told at once and has 3 working days to reply; HR then decides.'),
        style: Theme.of(context).textTheme.labelSmall,
      ),
      if (fixed != null)
        TextFormField(initialValue: fixed.fullName, enabled: false, decoration: InputDecoration(labelText: s.employee))
      else
        PickerField(
          label: s.employee,
          value: employee?.fullName,
          onTap: () async {
            final x = await pickEmployee(context, ref);
            if (x != null) setState(() => employee = x);
          },
        ),
      warningLevelField(context, level, (v) => setState(() => level = v)),
      TextFormField(
        controller: violationType,
        maxLength: 200,
        decoration: InputDecoration(labelText: s.t('نوع المخالفة', 'Violation type')),
        validator: (v) => requiredValidator(context, v),
      ),
      PickerField(label: s.t('تاريخ المخالفة', 'Violation date'), value: date == null ? null : f.date(date), icon: Icons.event, onTap: () async {
        final x = await pickDate(context, initial: date, last: DateTime.now());
        if (x != null) setState(() => date = x);
      }),
      TextFormField(
        controller: description,
        maxLines: 5,
        maxLength: 4000,
        decoration: InputDecoration(labelText: s.t('تفاصيل المخالفة', 'Violation details'), alignLabelWithHint: true),
        validator: (v) => requiredValidator(context, v),
      ),
      TextFormField(
        controller: note,
        maxLines: 2,
        maxLength: 2000,
        decoration: InputDecoration(labelText: s.note, helperText: s.t('تظهر للموظف مع الإنذار.', 'The employee sees it with the warning.')),
      ),
    ],
    onSubmit: () async {
      if (employee == null || date == null) {
        showError(context, s.validation);
        return false;
      }
      return runAction(context, () async {
        final created = await ref.read(hrApiProvider).warnings.Warning_CreateByHr(
              idempotencyKey: key,
              body: WarningHrCreateInputDto(
                employeeId: employee!.id,
                level: level,
                violationType: violationType.text.trim(),
                incidentDate: date!,
                description: description.text.trim(),
                note: optText(note),
              ),
            );
        createdId = created.id;
      }, done: s.t('أُرسل الإنذار', 'Warning sent'));
    },
  );
  return createdId;
}

String warningStatusLabel(S s, WarningStatus st) => switch (st) {
      WarningStatus.recommended => s.t('توصية', 'Recommended'),
      WarningStatus.notified => s.t('أُبلغ الموظف', 'Notified'),
      WarningStatus.responded => s.t('ردّ الموظف', 'Responded'),
      WarningStatus.responseExpired => s.t('انتهت مهلة الرد', 'Response expired'),
      WarningStatus.issued => s.t('صدر الإنذار', 'Issued'),
      WarningStatus.dismissed => s.t('حُفظت', 'Dismissed'),
      WarningStatus.objected => s.t('معترض عليه', 'Objected'),
      WarningStatus.objectionUpheld => s.t('ثُبّت بعد الاعتراض', 'Objection upheld'),
      WarningStatus.objectionOverturned => s.t('أُلغي بعد الاعتراض', 'Objection overturned'),
      _ => '—',
    };

Tone warningTone(WarningStatus st) => switch (st) {
      WarningStatus.issued || WarningStatus.objectionUpheld => Tone.danger,
      WarningStatus.dismissed || WarningStatus.objectionOverturned => Tone.neutral,
      WarningStatus.objected => Tone.warning,
      _ => Tone.info,
    };

String stageLabel(S s, WarningRecommendationStage st) => switch (st) {
      WarningRecommendationStage.underReview => s.t('قيد المراجعة', 'Under review'),
      WarningRecommendationStage.issued => s.t('صدر', 'Issued'),
      WarningRecommendationStage.notIssued => s.t('لم يصدر', 'Not issued'),
      _ => '—',
    };

/// S-WRN: warnings — my warnings (employee), my recommendations (department manager; the reviewer no longer
/// recommends, decision 2026-11-14),
/// all (HR) and objections (executive director).
class WarningsPage extends ConsumerStatefulWidget {
  const WarningsPage({super.key});

  @override
  ConsumerState<WarningsPage> createState() => _WarningsPageState();
}

class _WarningsPageState extends ConsumerState<WarningsPage> {
  int _version = 0;
  WarningStatus? _status;
  bool _pendingOnly = true;

  Future<void> _recommend() async {
    final s = S.of(context);
    final f = Fmt(context);
    EmployeeRefDto? employee;
    var level = WarningLevel.first;
    DateTime? date;
    final description = TextEditingController();
    final files = <PlatformFile>[];
    final key = const Uuid().v4();
    final ok = await showFormDialog(
      context,
      width: 600,
      title: s.t('توصية إنذار', 'Warning recommendation'),
      fields: (setState) => [
        Text(
          s.t('توصية على موظفيك المرتبطين بك. لا يصل شيء للموظف قبل مراجعة الموارد البشرية.',
              'On your own team. Nothing reaches the employee before HR reviews it.'),
          style: Theme.of(context).textTheme.labelSmall,
        ),
        PickerField(
          label: s.employee,
          value: employee?.fullName,
          onTap: () async {
            final x = await pickEmployee(context, ref, teamOnly: true);
            if (x != null) setState(() => employee = x);
          },
        ),
        warningLevelField(context, level, (v) => setState(() => level = v)),
        PickerField(label: s.t('تاريخ الواقعة', 'Incident date'), value: date == null ? null : f.date(date), icon: Icons.event, onTap: () async {
          final x = await pickDate(context, initial: date, last: DateTime.now());
          if (x != null) setState(() => date = x);
        }),
        TextFormField(
          controller: description,
          maxLines: 5,
          maxLength: 4000,
          decoration: InputDecoration(labelText: s.t('وصف الواقعة', 'Incident description'), alignLabelWithHint: true),
          validator: (v) => requiredValidator(context, v),
        ),
        Wrap(spacing: 6, runSpacing: 6, children: [
          for (final x in files) InputChip(label: Text(x.name), onDeleted: () => setState(() => files.remove(x))),
          ActionChip(
            avatar: const Icon(Icons.attach_file, size: 18),
            label: Text(s.addAttachment),
            onPressed: () async {
              final picked = await pickFiles(multiple: true);
              setState(() => files.addAll(picked));
            },
          ),
        ]),
      ],
      onSubmit: () async {
        if (employee == null || date == null) {
          showError(context, s.validation);
          return false;
        }
        return runAction(
          context,
          () => ref.read(hrApiProvider).warnings.Warning_Create(
                idempotencyKey: key,
                body: WarningCreateInputDto(
                  employeeId: employee!.id,
                  recommendedLevel: level,
                  incidentDate: date!,
                  description: description.text.trim(),
                  attachments: files.isEmpty ? null : [for (final x in files) multipartOf(x)],
                ),
              ),
          done: s.submitted,
        );
      },
    );
    if (ok) setState(() => _version++);
  }

  Widget _tile(BuildContext context, WarningDto w) {
    final s = S.of(context);
    final f = Fmt(context);
    return OpsTile(
      leading: Icons.report_outlined,
      avatar: PersonAvatar.employee(w.employee),
      title: '${w.employee.fullName} · ${warningLevelLabel(s, w.decision?.issuedLevel ?? w.recommendedLevel)}',
      subtitle: [f.date(w.incidentDate), if (w.violationType != null) w.violationType!, w.recommendedBy.displayName, if (w.employee.departmentName != null) w.employee.departmentName!].join(' · '),
      trailing: StatusPill(warningStatusLabel(s, w.status), tone: warningTone(w.status)),
      onTap: () => context.go('/warnings/${w.id}'),
    );
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context);
    final f = Fmt(context);
    final session = ref.watch(sessionProvider);
    final api = ref.watch(hrApiProvider).warnings;
    final tabs = <(String, Widget)>[
      if (session.can(HrOps.Warning_List))
        (
          s.t('كل الإنذارات', 'All warnings'),
          PagedList<WarningDto>(
            key: ValueKey('all|$_version|$_status'),
            pageSize: 50,
            header: Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Align(
                alignment: AlignmentDirectional.centerStart,
                child: enumFilter<WarningStatus>(context,
                    label: s.status, value: _status, values: WarningStatus.values, text: (v) => warningStatusLabel(s, v), onChanged: (v) => setState(() => _status = v)),
              ),
            ),
            fetch: (skip, max) async {
              final r = await api.Warning_List(skipCount: skip, maxResultCount: max, status: _status, sorting: 'creationTime desc');
              return (r.items, r.totalCount);
            },
            itemBuilder: _tile,
          )
        ),
      if (session.can(HrOps.Warning_GetObjections))
        (
          s.t('الاعتراضات', 'Objections'),
          PagedList<WarningDto>(
            key: ValueKey('obj|$_version|$_pendingOnly'),
            header: Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Wrap(children: [
                FilterChip(label: Text(s.t('قيد البت فقط', 'Pending only')), selected: _pendingOnly, onSelected: (v) => setState(() => _pendingOnly = v)),
              ]),
            ),
            fetch: (skip, max) async {
              final r = await api.Warning_GetObjections(skipCount: skip, maxResultCount: max, isPending: _pendingOnly ? true : null);
              return (r.items, r.totalCount);
            },
            itemBuilder: _tile,
          )
        ),
      if (session.can(HrOps.Warning_GetRecommendedByMe))
        (
          s.t('توصياتي', 'My recommendations'),
          PagedList<WarningRecommendationDto>(
            key: ValueKey('rec|$_version'),
            fetch: (skip, max) async {
              final r = await api.Warning_GetRecommendedByMe(skipCount: skip, maxResultCount: max, sorting: 'creationTime desc');
              return (r.items, r.totalCount);
            },
            itemBuilder: (context, w) => OpsTile(
              leading: Icons.outlined_flag,
              avatar: PersonAvatar.employee(w.employee),
              title: '${w.employee.fullName} · ${warningLevelLabel(s, w.recommendedLevel)}',
              subtitle: [f.date(w.incidentDate), w.description].join(' · '),
              trailing: StatusPill(stageLabel(s, w.stage), tone: w.stage == WarningRecommendationStage.issued ? Tone.danger : Tone.info),
            ),
          )
        ),
      if (session.can(HrOps.Warning_GetMyList))
        (
          s.t('إنذاراتي', 'My warnings'),
          PagedList<WarningEmployeeDto>(
            key: ValueKey('my|$_version'),
            emptyMessage: s.t('لا توجد إنذارات.', 'No warnings.'),
            fetch: (skip, max) async {
              final r = await api.Warning_GetMyList(skipCount: skip, maxResultCount: max);
              return (r.items, r.totalCount);
            },
            itemBuilder: (context, w) => OpsTile(
              leading: Icons.report_outlined,
              title: warningLevelLabel(s, w.level),
              subtitle: '${f.date(w.incidentDate)} · ${s.t('أُبلغت', 'Notified')} ${f.date(w.notifiedAt)}',
              trailing: StatusPill(warningStatusLabel(s, w.status), tone: warningTone(w.status)),
              onTap: () => context.go('/warnings/my/${w.id}'),
            ),
          )
        ),
    ];
    return HrPage(
      title: s.t('الإنذارات', 'Warnings'),
      actions: [
        if (session.can(HrOps.Warning_CreateByHr))
          FilledButton.icon(
            onPressed: () async {
              if (await showHrWarningDialog(context, ref) != null) setState(() => _version++);
            },
            icon: const Icon(Icons.report_outlined),
            label: Text(s.t('إرسال إنذار', 'Send a warning')),
          ),
        if (session.can(HrOps.Warning_Create))
          FilledButton.icon(onPressed: _recommend, icon: const Icon(Icons.add), label: Text(s.t('توصية إنذار', 'Recommend warning'))),
      ],
      child: tabbedBody(context, tabs),
    );
  }
}

final warningProvider = FutureProvider.autoDispose.family<WarningDto, String>((ref, id) => ref.watch(hrApiProvider).warnings.Warning_Get(id: id));

/// Full warning (HR, and executive director for objections): notify, decide, resolve objection.
class WarningDetailPage extends ConsumerWidget {
  const WarningDetailPage({super.key, required this.id});
  final String id;

  Future<void> _decide(BuildContext context, WidgetRef ref, WarningDto w) async {
    final s = S.of(context);
    var outcome = WarningDecisionOutcome.issue;
    var level = w.recommendedLevel;
    final note = TextEditingController();
    final ok = await showFormDialog(
      context,
      title: s.t('قرار الموارد البشرية', 'HR decision'),
      fields: (setState) => [
        SegmentedButton<WarningDecisionOutcome>(
          segments: [
            ButtonSegment(value: WarningDecisionOutcome.issue, label: Text(s.t('إصدار الإنذار', 'Issue'))),
            ButtonSegment(value: WarningDecisionOutcome.dismiss, label: Text(s.t('حفظ', 'Dismiss'))),
          ],
          selected: {outcome},
          onSelectionChanged: (v) => setState(() => outcome = v.first),
        ),
        if (outcome == WarningDecisionOutcome.issue)
          warningLevelField(context, level, (v) => setState(() => level = v)),
        TextFormField(
          controller: note,
          maxLines: 3,
          maxLength: 2000,
          decoration: InputDecoration(labelText: s.t('ملاحظة القرار', 'Decision note')),
          validator: (v) => requiredValidator(context, v),
        ),
      ],
      onSubmit: () => runAction(
        context,
        () => ref.read(hrApiProvider).warnings.Warning_Decide(
            id: w.id,
            body: WarningDecideInputDto(decision: outcome, issuedLevel: outcome == WarningDecisionOutcome.issue ? level : null, note: note.text.trim())),
        done: s.saved,
      ),
    );
    if (ok) ref.invalidate(warningProvider(id));
  }

  Future<void> _resolve(BuildContext context, WidgetRef ref, WarningDto w) async {
    final s = S.of(context);
    var outcome = WarningObjectionOutcome.uphold;
    final note = TextEditingController();
    final ok = await showFormDialog(
      context,
      title: s.t('البت في الاعتراض (نهائي)', 'Resolve objection (final)'),
      fields: (setState) => [
        SegmentedButton<WarningObjectionOutcome>(
          segments: [
            ButtonSegment(value: WarningObjectionOutcome.uphold, label: Text(s.t('تثبيت الإنذار', 'Uphold'))),
            ButtonSegment(value: WarningObjectionOutcome.overturn, label: Text(s.t('إلغاء الإنذار', 'Overturn'))),
          ],
          selected: {outcome},
          onSelectionChanged: (v) => setState(() => outcome = v.first),
        ),
        TextFormField(controller: note, maxLines: 3, maxLength: 2000, decoration: InputDecoration(labelText: s.note), validator: (v) => requiredValidator(context, v)),
      ],
      onSubmit: () => runAction(context,
          () => ref.read(hrApiProvider).warnings.Warning_ResolveObjection(id: w.id, body: WarningResolveObjectionInputDto(outcome: outcome, note: note.text.trim())),
          done: s.saved),
    );
    if (ok) ref.invalidate(warningProvider(id));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = S.of(context);
    final f = Fmt(context);
    final session = ref.watch(sessionProvider);
    return HrPage(
      title: s.t('الإنذار', 'Warning'),
      child: AsyncBody<WarningDto>(
        value: ref.watch(warningProvider(id)),
        onRetry: () => ref.invalidate(warningProvider(id)),
        builder: (w) => ListView(padding: const EdgeInsets.only(bottom: 24), children: [
          HrCard(
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              SectionTitle(w.employee.fullName, trailing: StatusPill(warningStatusLabel(s, w.status), tone: warningTone(w.status))),
              InfoRow(s.t('المستوى الموصى به', 'Recommended level'), warningLevelLabel(s, w.recommendedLevel)),
              InfoRow(s.t('تاريخ الواقعة', 'Incident date'), f.date(w.incidentDate)),
              if (w.violationType != null) InfoRow(s.t('نوع المخالفة', 'Violation type'), w.violationType!),
              InfoRow(s.t('صاحب التوصية', 'Recommended by'), '${w.recommendedBy.displayName} · ${f.dateTime(w.creationTime)}'),
              if (w.notifiedAt != null) InfoRow(s.t('أُبلغ', 'Notified'), f.dateTime(w.notifiedAt)),
              if (w.responseDueDate != null) InfoRow(s.t('مهلة الرد', 'Response due'), f.date(w.responseDueDate)),
              const Divider(),
              Text(w.description),
              if (w.note != null) ...[const SizedBox(height: 8), InfoRow(s.note, w.note!)],
              if (w.attachments.isNotEmpty) ...[
                const SizedBox(height: 8),
                Wrap(spacing: 6, runSpacing: 6, children: [
                  for (final a in w.attachments)
                    ActionChip(
                      avatar: const Icon(Icons.visibility_outlined, size: 18),
                      label: Text(a.fileName),
                      onPressed: () => previewLink(
                          context, () => ref.read(hrApiProvider).warnings.Warning_GetAttachmentDownloadUrl(id: w.id, attachmentId: a.id),
                          fileName: a.fileName),
                    ),
                ]),
              ],
            ]),
          ),
          if (w.employeeResponse != null) ...[
            const SizedBox(height: 12),
            HrCard(
              child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                SectionTitle(s.t('رد الموظف', 'Employee response')),
                Text(w.employeeResponse!.content),
                Text(f.dateTime(w.employeeResponse!.submittedAt), style: TextStyle(color: context.hr.textSecondary, fontSize: 12)),
              ]),
            ),
          ],
          if (w.decision != null) ...[
            const SizedBox(height: 12),
            HrCard(
              child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                SectionTitle(s.t('القرار', 'Decision')),
                InfoRow(s.t('القرار', 'Decision'),
                    w.decision!.outcome == WarningDecisionOutcome.issue ? '${s.t('إصدار', 'Issue')} · ${warningLevelLabel(s, w.decision!.issuedLevel)}' : s.t('حفظ', 'Dismiss')),
                InfoRow(s.note, w.decision!.note),
                InfoRow(s.t('بواسطة', 'By'), '${w.decision!.decidedBy.displayName} · ${f.dateTime(w.decision!.decidedAt)}'),
              ]),
            ),
          ],
          if (w.objection != null) ...[
            const SizedBox(height: 12),
            HrCard(
              child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
                SectionTitle(s.t('الاعتراض', 'Objection')),
                Text(w.objection!.content),
                Text(f.dateTime(w.objection!.submittedAt), style: TextStyle(color: context.hr.textSecondary, fontSize: 12)),
                if (w.objection!.outcome != null)
                  InfoRow(
                      s.t('النتيجة', 'Outcome'),
                      '${w.objection!.outcome == WarningObjectionOutcome.uphold ? s.t('ثُبّت', 'Upheld') : s.t('أُلغي', 'Overturned')}'
                          '${w.objection!.resolutionNote == null ? '' : ' · ${w.objection!.resolutionNote}'}'
                          '${w.objection!.resolvedBy == null ? '' : ' · ${w.objection!.resolvedBy!.displayName}'}'),
              ]),
            ),
          ],
          const SizedBox(height: 12),
          Wrap(spacing: 8, runSpacing: 8, children: [
            if (w.status == WarningStatus.recommended && session.can(HrOps.Warning_Notify))
              FilledButton(
                onPressed: () async {
                  if (!await confirmAction(context,
                      title: s.t('إبلاغ الموظف', 'Notify employee'), message: s.t('تبدأ مهلة رد مدتها 3 أيام دوام.', 'A 3 working-day response window starts.'))) {
                    return;
                  }
                  if (context.mounted && await runAction(context, () => ref.read(hrApiProvider).warnings.Warning_Notify(id: w.id), done: s.done)) {
                    ref.invalidate(warningProvider(id));
                  }
                },
                child: Text(s.t('تسجيل الواقعة وإبلاغ الموظف', 'Record and notify')),
              ),
            if ((w.status == WarningStatus.responded || w.status == WarningStatus.responseExpired) && session.can(HrOps.Warning_Decide))
              FilledButton(onPressed: () => _decide(context, ref, w), child: Text(s.t('إصدار القرار', 'Decide'))),
            if (w.status == WarningStatus.objected && session.can(HrOps.Warning_ResolveObjection))
              FilledButton(onPressed: () => _resolve(context, ref, w), child: Text(s.t('البت في الاعتراض', 'Resolve objection'))),
          ]),
        ]),
      ),
    );
  }
}

final myWarningProvider = FutureProvider.autoDispose.family<WarningEmployeeDto, String>((ref, id) => ref.watch(hrApiProvider).warnings.Warning_GetMy(id: id));

/// The employee's view: respond once within the window, object once after the decision.
class MyWarningPage extends ConsumerWidget {
  const MyWarningPage({super.key, required this.id});
  final String id;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final s = S.of(context);
    final f = Fmt(context);
    final api = ref.read(hrApiProvider).warnings;
    return HrPage(
      title: s.t('إنذار', 'Warning'),
      child: AsyncBody<WarningEmployeeDto>(
        value: ref.watch(myWarningProvider(id)),
        onRetry: () => ref.invalidate(myWarningProvider(id)),
        builder: (w) => ListView(padding: const EdgeInsets.only(bottom: 24), children: [
          HrCard(
            child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
              SectionTitle(warningLevelLabel(s, w.level), trailing: StatusPill(warningStatusLabel(s, w.status), tone: warningTone(w.status))),
              if (w.violationType != null) InfoRow(s.t('نوع المخالفة', 'Violation type'), w.violationType!),
              InfoRow(s.t('تاريخ الواقعة', 'Incident date'), f.date(w.incidentDate)),
              InfoRow(s.t('أُبلغت', 'Notified'), f.dateTime(w.notifiedAt)),
              InfoRow(s.t('مهلة الرد', 'Response due'), f.date(w.responseDueDate)),
              const Divider(),
              Text(w.description),
              if (w.note != null) ...[const SizedBox(height: 8), InfoRow(s.note, w.note!)],
              if (w.myResponse != null) ...[const Divider(), InfoRow(s.t('ردي', 'My response'), w.myResponse!.content)],
              if (w.decisionNote != null) ...[const Divider(), InfoRow(s.t('القرار', 'Decision'), '${w.decisionNote} · ${f.date(w.decidedAt)}')],
              if (w.myObjection != null) ...[
                const Divider(),
                InfoRow(s.t('اعتراضي', 'My objection'), w.myObjection!.content),
                if (w.myObjection!.outcome != null)
                  InfoRow(s.t('نتيجة الاعتراض', 'Objection outcome'),
                      '${w.myObjection!.outcome == WarningObjectionOutcome.uphold ? s.t('ثُبّت الإنذار', 'Upheld') : s.t('أُلغي الإنذار', 'Overturned')}${w.myObjection!.resolutionNote == null ? '' : ' · ${w.myObjection!.resolutionNote}'}'),
              ],
            ]),
          ),
          const SizedBox(height: 12),
          Wrap(spacing: 8, runSpacing: 8, children: [
            if (w.canRespond)
              FilledButton.icon(
                onPressed: () async {
                  final text = await askText(context, title: s.t('الرد على الإنذار (مرة واحدة)', 'Respond (once)'), label: s.description, maxLength: 4000);
                  if (text == null || !context.mounted) return;
                  if (await runAction(context, () => api.Warning_Respond(id: id, body: WarningRespondInputDto(content: text)), done: s.submitted)) {
                    ref.invalidate(myWarningProvider(id));
                  }
                },
                icon: const Icon(Icons.reply),
                label: Text(s.t('الرد', 'Respond')),
              ),
            if (w.canObject)
              OutlinedButton.icon(
                onPressed: () async {
                  final text = await askText(context, title: s.t('الاعتراض (مرة واحدة)', 'Object (once)'), label: s.description, maxLength: 4000);
                  if (text == null || !context.mounted) return;
                  if (await runAction(context, () => api.Warning_Object(id: id, body: WarningObjectInputDto(content: text)), done: s.submitted)) {
                    ref.invalidate(myWarningProvider(id));
                  }
                },
                icon: const Icon(Icons.gavel_outlined),
                label: Text(s.t('اعتراض', 'Object')),
              ),
          ]),
        ]),
      ),
    );
  }
}

/// «إرسال إنذار» in the HR manager's sidebar (decision 2026-11-17): send a warning, and follow the latest ones.
class SendWarningPage extends ConsumerStatefulWidget {
  const SendWarningPage({super.key});

  @override
  ConsumerState<SendWarningPage> createState() => _SendWarningPageState();
}

class _SendWarningPageState extends ConsumerState<SendWarningPage> {
  int _version = 0;

  @override
  Widget build(BuildContext context) {
    final s = S.of(context);
    final f = Fmt(context);
    final api = ref.watch(hrApiProvider).warnings;
    return HrPage(
      title: s.t('إرسال إنذار', 'Send a warning'),
      actions: [
        FilledButton.icon(
          onPressed: () async {
            final id = await showHrWarningDialog(context, ref);
            if (id != null) setState(() => _version++);
          },
          icon: const Icon(Icons.add),
          label: Text(s.t('إنذار جديد', 'New warning')),
        ),
      ],
      child: PagedList<WarningDto>(
        key: ValueKey('sent|$_version'),
        pageSize: 50,
        header: Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: Text(s.t('أحدث الإنذارات. افتح الإنذار لإصدار القرار بعد رد الموظف أو انتهاء مهلته.',
              'Latest warnings. Open one to decide after the reply or once its window ends.'),
              style: Theme.of(context).textTheme.labelSmall),
        ),
        fetch: (skip, max) async {
          final r = await api.Warning_List(skipCount: skip, maxResultCount: max, sorting: 'creationTime desc');
          return (r.items, r.totalCount);
        },
        itemBuilder: (context, w) => OpsTile(
          leading: Icons.report_outlined,
          avatar: PersonAvatar.employee(w.employee),
          title: '${w.employee.fullName} · ${warningLevelLabel(s, w.decision?.issuedLevel ?? w.recommendedLevel)}',
          subtitle: [f.date(w.incidentDate), if (w.violationType != null) w.violationType!, w.recommendedBy.displayName].join(' · '),
          trailing: StatusPill(warningStatusLabel(s, w.status), tone: warningTone(w.status)),
          onTap: () => context.go('/warnings/${w.id}'),
        ),
      ),
    );
  }
}
