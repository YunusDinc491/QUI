# 03 — Veri Modeli (taslak)

> Alan adları İngilizce (kod kuralı). Bu taslak yapım sırasında (ilgili fazda) tamamlanır; ana yapı kararlıdır. PostgreSQL; zaman alanları UTC.

## Kullanıcılar

| Tablo | Önemli alanlar |
|---|---|
| `users` | id, firebase_uid (unique), username (unique, değişmez), display_name, avatar_id, frame_id, birth_year, status (active/banned/deleted), total_score, created_at, deleted_at |
| `user_devices` | user_id, device_id, platform, last_seen_at, integrity_flag |
| `user_avatars` | user_id, avatar_id (açılanlar) |
| `user_frames` | user_id, frame_id (açılanlar) |
| `user_jokers` | user_id, joker_type, count |
| `avatars`, `frames` | id, name, image_url, price (0 = ücretsiz), is_active |
| `bans_warnings` | user_id, type, reason, created_by_admin, created_at |

## İçerik

| Tablo | Önemli alanlar |
|---|---|
| `modes` | id, code (general/branch/kpss/fan), name, order |
| `categories` | id, mode_id, name, image_url, color, order, is_published |
| `questions` | id, category_id, text, option_a..d, correct_option, difficulty (easy/medium/hard), explanation (nullable), status (draft/published/archived), shown_count, correct_count, report_count, created_at |
| `question_reports` | id, question_id, user_id, reason, created_at, status |
| `user_seen_questions` | user_id, question_id, seen_at (son 200 tutulur) |
| `question_suggestions` | id, user_id, content, status, converted_question_id |

## Maç

| Tablo | Önemli alanlar |
|---|---|
| `matches` | id, type (solo/random/bot/friend), mode_id, category_id, status (created/countdown/running/finished/cancelled/forfeited), started_at, ended_at, period_id, winner |
| `match_players` | match_id, user_id (nullable) / bot_profile_id (nullable), correct_count, points, result (win/lose/draw), disconnect_ms_used, doubled (bool) |
| `match_questions` | match_id, position, question_id, difficulty_slot, option_order |
| `match_answers` | match_id, player_id, position, chosen_option, is_correct, response_ms, joker_used |
| `bot_profiles` | id, name, avatar_id, frame_id, power_level, speed_min_ms, speed_max_ms, history |
| `rooms` | code, owner_id, mode_id, category_id, expires_at, status |

## Puanlama ve turnuva

| Tablo | Önemli alanlar |
|---|---|
| `scoring_rules` | mode_id, solo_ladder (json), versus_win, versus_bonus, bonus_threshold, draw_coefficient, joker_hint_probability… |
| `tournament_periods` | id, starts_at, ends_at, status (open/review/closed/distributed), distributed_at |
| `period_scores` | period_id, user_id, points, match_count |
| `period_reward_table` | rank_from, rank_to, reward_amount |
| `disqualifications` | period_id, user_id, reason, admin_id, created_at |

## Cüzdan ve mağaza

| Tablo | Önemli alanlar |
|---|---|
| `wallet_ledger` | id, user_id, amount (+/-), type (tournament/purchase/refund/admin/…), ref_type, ref_id, idempotency_key (unique), created_at |
| `joker_ledger` | id, user_id, joker_type, delta, type (gift/ad/purchase/use/admin), ref_id, idempotency_key (unique), created_at |
| `store_products` | id, type (coupon/joker/avatar/frame), name, price, image_url, is_active, stock_mode |
| `coupon_codes` | id, product_id, code_encrypted, status (available/reserved/sold/invalid), sold_to, sold_at, note |
| `user_rewards` | id, user_id, product_id, coupon_code_id, created_at, revealed_at, reveal_count, issue_reported_at, replaced_count |

## Reklam

| Tablo | Önemli alanlar |
|---|---|
| `ad_intents` | id, user_id, purpose (joker/continue/double), ref_id, status (created/verified/consumed/expired), transaction_id (unique), created_at |

## Destek ve bildirim

| Tablo | Önemli alanlar |
|---|---|
| `support_tickets` | id, user_id, category, subject, status (open/answered/closed), created_at |
| `ticket_messages` | id, ticket_id, sender (user/admin), body, created_at |
| `ticket_attachments` | id, message_id, file_url, size |
| `notifications` | id, user_id, type, title, body, created_at, read_at |
| `push_tokens` | user_id, token, device_id, updated_at |
| `announcements` | id, title, body, target (all/min_version), created_at |

## Yönetim

| Tablo | Önemli alanlar |
|---|---|
| `admin_users` | id, email, password_hash, totp_secret, role |
| `audit_logs` | id, admin_id, action, entity, entity_id, before, after, created_at |
| `app_config` | key, value (min_supported_version, maintenance_mode, hint_probability, difficulty_distribution, …) |
| `profanity_words` | id, word |

## Kurallar

- `wallet_ledger` ve `joker_ledger` **sadece eklenir**; bakiye defter toplamıdır (okuma için önbellek tutulabilir ama kaynak defterdir). `idempotency_key` aynı işlemin ikinci kez uygulanmasını engeller.
- Silinen kullanıcı: kişisel alanlar anonimleştirilir, maç satırları "Silinmiş kullanıcı" olarak kalır, sıralamadan çıkar.
- Saklama süreleri: ayrıntılı oyun/cevap logları 90 gün, maç özetleri 1 yıl, denetim kayıtları 2 yıl (08).
- Kupon kodları AES-GCM ile şifrelenir; anahtar Railway ortam değişkenindedir.
