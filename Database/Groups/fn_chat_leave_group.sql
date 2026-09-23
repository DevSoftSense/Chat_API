-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.
-- Last admin leave: auto-promote earliest remaining active member, then leave.

CREATE OR REPLACE FUNCTION public.fn_chat_leave_group(p_group_id bigint, p_user_id bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS TABLE(group_id bigint, user_id integer, is_active boolean, left_at timestamp with time zone)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_is_admin boolean;
    v_active_admin_count integer;
    v_next_admin_user_id integer;
    v_left timestamptz;
BEGIN
    IF p_group_id IS NULL OR p_group_id <= 0
       OR p_user_id IS NULL OR p_user_id <= 0
       OR p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_leave_group: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    PERFORM public.fn_chat_assert_group_member(
        p_group_id, p_user_id, p_org_id, p_app_id, p_fiscal_year_id);

    SELECT COALESCE(m.is_admin, false)
    INTO v_is_admin
    FROM public.tab_group_members m
    WHERE m.group_id = p_group_id
      AND m.user_id = p_user_id::integer
      AND m.left_at IS NULL;

    IF v_is_admin THEN
        SELECT COUNT(*)::integer
        INTO v_active_admin_count
        FROM public.tab_group_members m
        WHERE m.group_id = p_group_id
          AND m.left_at IS NULL
          AND COALESCE(m.is_admin, false) = true;

        -- Last admin: hand off admin to the earliest remaining active member.
        IF v_active_admin_count <= 1 THEN
            SELECT m.user_id
            INTO v_next_admin_user_id
            FROM public.tab_group_members m
            WHERE m.group_id = p_group_id
              AND m.user_id <> p_user_id::integer
              AND m.left_at IS NULL
            ORDER BY m.joined_on ASC NULLS LAST, m.user_id ASC
            LIMIT 1;

            IF v_next_admin_user_id IS NOT NULL THEN
                UPDATE public.tab_group_members m
                SET is_admin = true
                WHERE m.group_id = p_group_id
                  AND m.user_id = v_next_admin_user_id
                  AND m.left_at IS NULL;
            END IF;
            -- If no other members remain, allow leave (group has no active members).
        END IF;
    END IF;

    v_left := CURRENT_TIMESTAMP;

    UPDATE public.tab_group_members m
    SET left_at = v_left,
        is_admin = false
    WHERE m.group_id = p_group_id
      AND m.user_id = p_user_id::integer
      AND m.left_at IS NULL;

    group_id := p_group_id;
    user_id := p_user_id::integer;
    is_active := false;
    left_at := v_left;
    RETURN NEXT;
END;
$function$
