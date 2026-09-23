-- FIX: Group Star for any active member (demo user starring KK's group message).
-- Apply this in DBeaver / Postgres NOW, then retry Star in UI.
-- One-to-One path unchanged.

DROP FUNCTION IF EXISTS public.fn_chat_toggle_message_star(bigint, bigint, boolean, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_toggle_message_star(
    p_message_id bigint,
    p_user_id bigint,
    p_is_starred boolean,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer
)
RETURNS TABLE (
    message_id bigint,
    chat_id integer,
    sender_user_id bigint,
    receiver_user_id bigint,
    is_starred_by_me boolean,
    starred_at timestamp with time zone
)
LANGUAGE plpgsql
SECURITY INVOKER
AS $fn$
DECLARE
    v_msg public.tab_messages%ROWTYPE;
    v_now timestamptz := CURRENT_TIMESTAMP;
    v_is_sender boolean;
    v_starred boolean;
    v_starred_at timestamptz;
    v_group_id bigint;
    v_is_group_member boolean := false;
BEGIN
    IF p_message_id IS NULL
       OR p_user_id IS NULL
       OR p_is_starred IS NULL
       OR p_org_id IS NULL
       OR p_app_id IS NULL
       OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_toggle_message_star: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    SELECT *
    INTO v_msg
    FROM public.tab_messages m
    WHERE m.message_id = p_message_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(m.is_draft, false) = false;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'fn_chat_toggle_message_star: message % not found', p_message_id
            USING ERRCODE = 'P0002';
    END IF;

    IF COALESCE(v_msg.delete_flag, 0) <> 0 OR v_msg.deleted_at IS NOT NULL THEN
        RAISE EXCEPTION 'fn_chat_toggle_message_star: message % is deleted', p_message_id
            USING ERRCODE = '22023';
    END IF;

    v_starred := p_is_starred;
    v_starred_at := CASE WHEN p_is_starred THEN v_now ELSE NULL END;

    -- Resolve group: message.group_id OR tab_groups by chat_id (legacy rows).
    v_group_id := v_msg.group_id;
    IF v_group_id IS NULL THEN
        SELECT g.group_id
        INTO v_group_id
        FROM public.tab_groups g
        WHERE g.chat_id = v_msg.chat_id
          AND g.org_id = p_org_id
          AND g.app_id = p_app_id
          AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND COALESCE(g.is_deleted, false) = false
        LIMIT 1;
    END IF;

    -- ─── GROUP PATH ──────────────────────────────────────────────────────────
    IF v_group_id IS NOT NULL THEN
        SELECT EXISTS (
            SELECT 1
            FROM public.tab_group_members m
            WHERE m.group_id = v_group_id
              AND m.user_id = p_user_id::integer
              AND m.left_at IS NULL
              AND m.org_id = p_org_id
              AND m.app_id = p_app_id
              AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
        )
        INTO v_is_group_member;

        IF NOT COALESCE(v_is_group_member, false) THEN
            RAISE EXCEPTION 'fn_chat_toggle_message_star: user % is not an active member of group %',
                p_user_id, v_group_id
                USING ERRCODE = '42501';
        END IF;

        -- Backfill group_id on message if missing (legacy).
        IF v_msg.group_id IS NULL THEN
            UPDATE public.tab_messages m
            SET group_id = v_group_id,
                updated_at = v_now
            WHERE m.message_id = p_message_id;
            v_msg.group_id := v_group_id;
        END IF;

        DELETE FROM public.tab_message_stars s
        WHERE s.message_id = p_message_id
          AND s.user_id = p_user_id::integer
          AND s.org_id = p_org_id
          AND s.app_id = p_app_id
          AND s.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id;

        IF p_is_starred THEN
            INSERT INTO public.tab_message_stars (
                message_id,
                user_id,
                app_id,
                starred_at,
                org_id,
                fiscal_year_id
            )
            VALUES (
                p_message_id,
                p_user_id::integer,
                p_app_id,
                v_now,
                p_org_id,
                p_fiscal_year_id
            );
        END IF;

        message_id := v_msg.message_id;
        chat_id := v_msg.chat_id;
        sender_user_id := v_msg.sender_user_id;
        receiver_user_id := v_msg.receiver_user_id;
        is_starred_by_me := v_starred;
        starred_at := v_starred_at;
        RETURN NEXT;
        RETURN;
    END IF;

    -- ─── ONE-TO-ONE PATH (unchanged) ─────────────────────────────────────────
    IF v_msg.sender_user_id IS NOT DISTINCT FROM p_user_id THEN
        v_is_sender := true;
    ELSIF v_msg.receiver_user_id IS NOT DISTINCT FROM p_user_id THEN
        v_is_sender := false;
    ELSE
        RAISE EXCEPTION 'fn_chat_toggle_message_star: user % is not a participant of message %',
            p_user_id, p_message_id
            USING ERRCODE = '42501';
    END IF;

    IF v_is_sender THEN
        UPDATE public.tab_messages m
        SET is_starred_by_sender = v_starred,
            starred_by_sender_at = v_starred_at,
            updated_at = v_now
        WHERE m.message_id = p_message_id
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id;
    ELSE
        UPDATE public.tab_messages m
        SET is_starred_by_receiver = v_starred,
            starred_by_receiver_at = v_starred_at,
            updated_at = v_now
        WHERE m.message_id = p_message_id
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id;
    END IF;

    message_id := v_msg.message_id;
    chat_id := v_msg.chat_id;
    sender_user_id := v_msg.sender_user_id;
    receiver_user_id := v_msg.receiver_user_id;
    is_starred_by_me := v_starred;
    starred_at := v_starred_at;
    RETURN NEXT;
END;
$fn$;

COMMENT ON FUNCTION public.fn_chat_toggle_message_star(bigint, bigint, boolean, integer, integer, integer)
IS 'Stars/unstars for current user. One-to-One: sender/receiver columns. Group: tab_message_stars for any active member.';
