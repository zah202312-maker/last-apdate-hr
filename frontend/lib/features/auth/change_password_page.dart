import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/generated/hr_api.dart';
import '../../api/runtime/api_client.dart';
import '../../core/l10n/strings.dart';
import '../../core/network/api_providers.dart';
import '../../core/session/session_controller.dart';
import '../../core/theme/tokens.dart';
import '../../core/widgets/common.dart';
import 'auth_frame.dart';

/// S-AUTH-02 (forced) and S-ME-02 (voluntary): Account_ChangePassword.
class ChangePasswordPage extends ConsumerStatefulWidget {
  const ChangePasswordPage({super.key, required this.forced});
  final bool forced;

  @override
  ConsumerState<ChangePasswordPage> createState() => _ChangePasswordPageState();
}

class _ChangePasswordPageState extends ConsumerState<ChangePasswordPage> {
  final _form = GlobalKey<FormState>();
  final _current = TextEditingController();
  final _new = TextEditingController();
  final _confirm = TextEditingController();
  bool _busy = false;
  Object? _error;

  @override
  void dispose() {
    _current.dispose();
    _new.dispose();
    _confirm.dispose();
    super.dispose();
  }

  /// Abp.Identity.Password.* settings from application-configuration.
  /// Defaults match the server: 8 characters with upper, lower and a digit.
  ({int minLen, bool upper, bool lower, bool digit, bool symbol}) _rules() {
    final settings = ref.read(sessionProvider).config?.setting.values ?? const <String, String?>{};
    bool on(String k, bool fallback) {
      final raw = settings['Abp.Identity.Password.$k'];
      return raw == null ? fallback : raw.toLowerCase() == 'true';
    }

    return (
      minLen: int.tryParse(settings['Abp.Identity.Password.RequiredLength'] ?? '') ?? 8,
      upper: on('RequireUppercase', true),
      lower: on('RequireLowercase', true),
      digit: on('RequireDigit', true),
      symbol: on('RequireNonAlphanumeric', false),
    );
  }

  /// The full rule, shown whenever the new password breaks any part of it.
  /// Always in Arabic, whatever the app language (owner's request).
  String _policyHint(S s) {
    final r = _rules();
    final parts = <String>[
      if (r.upper) 'حرف كبير',
      if (r.lower) 'حرف صغير',
      if (r.digit) 'رقم',
      if (r.symbol) 'رمز',
    ];
    final tail = parts.isEmpty ? '' : ' وتحتوي على ${_joinAr(parts)}';
    return 'يجب أن تتكون كلمة السر من ${r.minLen} أحرف على الأقل$tail';
  }

  static String _joinAr(List<String> p) => p.length < 2 ? p.join() : '${p.sublist(0, p.length - 1).join(' و')} و${p.last}';

  String _errorText(S s, Object e) =>
      e is ApiException && e.code == 'Hr:Identity:PasswordPolicyViolation' ? _policyHint(s) : s.error(e);

  String? _policy(String? v, S s) {
    if (v == null || v.isEmpty) return s.requiredField;
    final r = _rules();
    final ok = v.length >= r.minLen &&
        (!r.upper || RegExp(r'[A-Z]').hasMatch(v)) &&
        (!r.lower || RegExp(r'[a-z]').hasMatch(v)) &&
        (!r.digit || RegExp(r'\d').hasMatch(v)) &&
        (!r.symbol || RegExp(r'[^A-Za-z0-9]').hasMatch(v));
    return ok ? null : _policyHint(s);
  }

  Future<void> _submit() async {
    if (!_form.currentState!.validate()) return;
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final result = await ref.read(hrApiProvider).currentUser.Account_ChangePassword(
            body: ChangePasswordInputDto(currentPassword: _current.text, newPassword: _new.text),
          );
      if (!mounted) return;
      showDone(context, S.of(context).passwordChanged);
      await ref.read(sessionProvider.notifier).gateChanged(result.accessGate);
      if (!widget.forced && mounted) context.pop();
    } catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final s = S.of(context);
    final form = Form(
      key: _form,
      child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
        if (widget.forced) ...[
          Text(s.changePassword, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          Text(s.changePasswordRequired, style: TextStyle(color: context.hr.textSecondary)),
          const SizedBox(height: 20),
        ],
        TextFormField(
          controller: _current,
          obscureText: true,
          autofillHints: const [AutofillHints.password],
          decoration: InputDecoration(labelText: s.currentPassword),
          validator: (v) => (v == null || v.isEmpty) ? s.requiredField : null,
        ),
        const SizedBox(height: 12),
        TextFormField(
          controller: _new,
          obscureText: true,
          maxLength: 128,
          autofillHints: const [AutofillHints.newPassword],
          decoration: InputDecoration(labelText: s.newPassword, errorMaxLines: 3),
          validator: (v) => _policy(v, s),
        ),
        const SizedBox(height: 4),
        TextFormField(
          controller: _confirm,
          obscureText: true,
          decoration: InputDecoration(labelText: s.confirmPassword),
          validator: (v) => v != _new.text ? s.passwordsDoNotMatch : null,
        ),
        if (_error != null) ...[
          const SizedBox(height: 12),
          Text(_errorText(s, _error!), style: const TextStyle(color: HrColors.danger)),
        ],
        const SizedBox(height: 20),
        FilledButton(onPressed: _busy ? null : _submit, child: Text(s.save)),
        if (widget.forced)
          TextButton(onPressed: () => ref.read(sessionProvider.notifier).signOut(), child: Text(s.signOut)),
      ]),
    );
    if (widget.forced) return AuthFrame(child: form);
    return HrPage(title: s.changePassword, child: SingleChildScrollView(child: ConstrainedBox(constraints: const BoxConstraints(maxWidth: 480), child: HrCard(child: form))));
  }
}
