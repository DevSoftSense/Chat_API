-- Synced from live DB. Group path is additive; private path preserved.
-- WARNING: re-applying DROP/CREATE may replace live overloads — review before deploy.

DROP FUNCTION IF EXISTS public.fn_chat_toggle_message_reaction(bigint, bigint, character varying, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_toggle_message_reaction(p_message_id bigint, p_user_id bigint, p_reaction_code character varying, p_app_id integer, p_org_id integer, p_fiscal_year_id integer)
 RETURNS TABLE(message_id bigint, chat_id integer, sender_user_id bigint, receiver_user_id bigint, reaction_id bigint, reaction_code character varying, reaction_emoji character varying, reacted_on timestamp with time zone)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_msg public.tab_messages%ROWTYPE;
    v_now timestamptz := CURRENT_TIMESTAMP;
    v_code character varying(30);
    v_emoji character varying(30);
    v_existing_id bigint;
    v_existing_code character varying(30);
    v_out_reaction_id bigint;
    v_out_reaction_code character varying(30);
    v_out_reaction_emoji character varying(30);
    v_out_reacted_on timestamptz;
BEGIN
    IF p_message_id IS NULL OR p_user_id IS NULL OR p_app_id IS NULL OR p_org_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_toggle_message_reaction: required parameters cannot be null' USING ERRCODE = '22023';
    END IF;

    v_code := upper(NULLIF(btrim(COALESCE(p_reaction_code, '')), ''));
    IF v_code IS NOT NULL THEN
        v_emoji := CASE v_code
            WHEN 'LIKE' THEN '👍' WHEN 'LOVE' THEN '❤️' WHEN 'LAUGH' THEN '😂'
            WHEN 'WOW' THEN '😮' WHEN 'SAD' THEN '😢' WHEN 'THANKS' THEN '🙏' ELSE NULL END;
        IF v_emoji IS NULL THEN
            RAISE EXCEPTION 'fn_chat_toggle_message_reaction: invalid reaction_code "%". Allowed: LIKE, LOVE, LAUGH, WOW, SAD, THANKS', p_reaction_code USING ERRCODE = '22023';
        END IF;
    END IF;

    SELECT * INTO v_msg FROM public.tab_messages m
    WHERE m.message_id = p_message_id AND m.org_id = p_org_id AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id AND COALESCE(m.is_draft, false) = false;
    IF NOT FOUND THEN
        RAISE EXCEPTION 'fn_chat_toggle_message_reaction: message % not found', p_message_id USING ERRCODE = 'P0002';
    END IF;
    IF COALESCE(v_msg.delete_flag, 0) <> 0 OR v_msg.deleted_at IS NOT NULL THEN
        RAISE EXCEPTION 'fn_chat_toggle_message_reaction: message % is deleted', p_message_id USING ERRCODE = '22023';
    END IF;

    IF NOT public.fn_chat_user_can_access_message(v_msg, p_user_id, p_org_id, p_app_id, p_fiscal_year_id) THEN
        RAISE EXCEPTION 'fn_chat_toggle_message_reaction: user % is not a participant of message %', p_user_id, p_message_id USING ERRCODE = '42501';
    END IF;

    SELECT r.reaction_id, r.reaction_code INTO v_existing_id, v_existing_code
    FROM public.tab_message_reactions AS r
    WHERE r.message_id = p_message_id AND r.user_id = p_user_id::integer
      AND r.org_id = p_org_id AND r.app_id = p_app_id
      AND r.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
    ORDER BY r.reacted_on DESC NULLS LAST, r.reaction_id DESC LIMIT 1;

    IF v_code IS NULL OR (v_existing_id IS NOT NULL AND upper(COALESCE(v_existing_code, '')) = v_code) THEN
        IF v_existing_id IS NOT NULL THEN
            DELETE FROM public.tab_message_reactions AS r WHERE r.reaction_id = v_existing_id;
        END IF;
        v_out_reaction_id := NULL; v_out_reaction_code := NULL; v_out_reaction_emoji := NULL; v_out_reacted_on := NULL;
    ELSIF v_existing_id IS NOT NULL THEN
        UPDATE public.tab_message_reactions AS r
        SET reaction_code = v_code, reaction = v_emoji, reacted_on = v_now
        WHERE r.reaction_id = v_existing_id
        RETURNING r.reaction_id, r.reaction_code, r.reaction, r.reacted_on
        INTO v_out_reaction_id, v_out_reaction_code, v_out_reaction_emoji, v_out_reacted_on;
    ELSE
        INSERT INTO public.tab_message_reactions (message_id, user_id, app_id, reaction_code, reaction, reacted_on, org_id, fiscal_year_id)
        VALUES (p_message_id, p_user_id::integer, p_app_id, v_code, v_emoji, v_now, p_org_id, p_fiscal_year_id)
        RETURNING tab_message_reactions.reaction_id, tab_message_reactions.reaction_code, tab_message_reactions.reaction, tab_message_reactions.reacted_on
        INTO v_out_reaction_id, v_out_reaction_code, v_out_reaction_emoji, v_out_reacted_on;
    END IF;

    message_id := v_msg.message_id; chat_id := v_msg.chat_id;
    sender_user_id := v_msg.sender_user_id; receiver_user_id := v_msg.receiver_user_id;
    reaction_id := v_out_reaction_id; reaction_code := v_out_reaction_code;
    reaction_emoji := v_out_reaction_emoji; reacted_on := v_out_reacted_on;
    RETURN NEXT;
END;
$function$
