-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

CREATE OR REPLACE FUNCTION public.fn_chat_remove_group_member(p_group_id bigint, p_target_user_id bigint, p_requesting_user_id bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS TABLE(group_id bigint, user_id integer, is_active boolean, left_at timestamp with time zone)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_target_is_admin boolean;
    v_active_admin_count integer;
    v_left timestamptz;
BEGIN
    IF p_group_id IS NULL OR p_group_id <= 0
       OR p_target_user_id IS NULL OR p_target_user_id <= 0
       OR p_requesting_user_id IS NULL OR p_requesting_user_id <= 0
       OR p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_remove_group_member: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    PERFORM public.fn_chat_assert_group_admin(
        p_group_id, p_requesting_user_id, p_org_id, p_app_id, p_fiscal_year_id);

    SELECT COALESCE(m.is_admin, false)
    INTO v_target_is_admin
    FROM public.tab_group_members m
    WHERE m.group_id = p_group_id
      AND m.user_id = p_target_user_id::integer
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND m.left_at IS NULL;

    IF v_target_is_admin IS NULL THEN
        RAISE EXCEPTION 'fn_chat_remove_group_member: target user % is not an active member of group %',
            p_target_user_id, p_group_id
            USING ERRCODE = 'P0002';
    END IF;

    IF v_target_is_admin THEN
        SELECT COUNT(*)::integer
        INTO v_active_admin_count
        FROM public.tab_group_members m
        WHERE m.group_id = p_group_id
          AND m.left_at IS NULL
          AND COALESCE(m.is_admin, false) = true;

        IF v_active_admin_count <= 1 THEN
            RAISE EXCEPTION 'fn_chat_remove_group_member: cannot remove the last active admin'
                USING ERRCODE = '22023';
        END IF;
    END IF;

    v_left := CURRENT_TIMESTAMP;

    UPDATE public.tab_group_members m
    SET left_at = v_left
    WHERE m.group_id = p_group_id
      AND m.user_id = p_target_user_id::integer
      AND m.left_at IS NULL;

    group_id := p_group_id;
    user_id := p_target_user_id::integer;
    is_active := false;
    left_at := v_left;
    RETURN NEXT;
END;
$function$

