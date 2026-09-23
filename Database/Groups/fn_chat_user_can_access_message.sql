-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

CREATE OR REPLACE FUNCTION public.fn_chat_user_can_access_message(p_message tab_messages, p_user_id bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS boolean
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    IF p_message.sender_user_id IS NOT DISTINCT FROM p_user_id
       OR p_message.receiver_user_id IS NOT DISTINCT FROM p_user_id THEN
        RETURN true;
    END IF;

    IF p_message.group_id IS NOT NULL THEN
        RETURN EXISTS (
            SELECT 1
            FROM public.tab_group_members m
            INNER JOIN public.tab_groups g ON g.group_id = m.group_id
            WHERE m.group_id = p_message.group_id
              AND m.user_id = p_user_id::integer
              AND m.left_at IS NULL
              AND m.org_id = p_org_id
              AND m.app_id = p_app_id
              AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
              AND g.org_id = p_org_id
              AND g.app_id = p_app_id
              AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
              AND COALESCE(g.is_deleted, false) = false
        );
    END IF;

    RETURN false;
END;
$function$

