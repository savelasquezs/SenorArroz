BEGIN TRANSACTION READ ONLY;
SET LOCAL statement_timeout = '5s';
SET LOCAL lock_timeout = '1s';
SET LOCAL app.system_scope = 'on';

SELECT json_build_object(
    'capturedAtUtc', now(),
    'serverVersion', current_setting('server_version'),
    'readOnly', current_setting('transaction_read_only'),
    'schema', (
        SELECT json_object_agg(table_name || '.' || column_name, true)
        FROM information_schema.columns WHERE table_schema = 'public' AND (
            table_name = 'whatsapp_webhook_event' AND column_name IN ('processing_state', 'partition_aliases', 'protected_result')
            OR table_name = 'whatsapp_message' AND column_name IN ('ai_processing_status', 'ai_response_attention_version')
            OR table_name = 'order' AND column_name IN ('origin_branch_id', 'management_version'))),
    'realtimeTableInstalled', to_regclass('public.whatsapp_realtime_event') IS NOT NULL,
    'pgStatStatementsInstalled', EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_stat_statements'),
    'connections', (SELECT json_build_object('total', count(*), 'active', count(*) FILTER (WHERE state = 'active'),
        'waiting', count(*) FILTER (WHERE wait_event_type IS NOT NULL)) FROM pg_stat_activity WHERE datname = current_database()),
    'databaseCounters', (SELECT json_build_object('commits', xact_commit, 'rollbacks', xact_rollback,
        'blocksRead', blks_read, 'blocksHit', blks_hit, 'deadlocks', deadlocks, 'statsReset', stats_reset)
        FROM pg_stat_database WHERE datname = current_database()),
    'whatsappLast7Days', (SELECT json_build_object('messages', count(*),
        'incoming', count(*) FILTER (WHERE direction = 'inbound'), 'outgoing', count(*) FILTER (WHERE direction = 'outbound'),
        'manualAdvisors', count(DISTINCT sent_by_user_id) FILTER (WHERE direction = 'outbound'),
        'conversations', count(DISTINCT conversation_id),
        'averageStoredTextBytes', round(avg(octet_length(text_body))),
        'averageStoredPayloadBytes', round(avg(octet_length(raw_payload::text))))
        FROM whatsapp_message WHERE timestamp >= now() - interval '7 days'),
    'incomingPeak5MinutesLast7Days', (SELECT coalesce(max(messages), 0) FROM (
        SELECT count(*) AS messages FROM whatsapp_message WHERE timestamp >= now() - interval '7 days' AND direction = 'inbound'
        GROUP BY date_bin(interval '5 minutes', timestamp, timestamptz '2026-01-01 00:00:00+00')) buckets),
    'aiBacklog', (SELECT json_build_object('pending', count(*) FILTER (WHERE ai_processing_status = 'pending'),
        'processing', count(*) FILTER (WHERE ai_processing_status IN ('processing', 'responsegenerated', 'sending')),
        'failedLast24Hours', count(*) FILTER (WHERE ai_processing_status = 'failed' AND timestamp >= now() - interval '1 day'))
        FROM whatsapp_message),
    'attention', (SELECT json_build_object('assignedConversations', count(*) FILTER (WHERE assigned_user_id IS NOT NULL),
        'assignedAdvisors', count(DISTINCT assigned_user_id),
        'waitingForHuman', count(*) FILTER (WHERE attention_mode = 'waitingforhuman')) FROM whatsapp_conversation)
);

ROLLBACK;
