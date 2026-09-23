-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

CREATE OR REPLACE FUNCTION public.fn_chat_assert_group_admin(p_group_id bigint, p_user_id bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS void
 LANGUAGE plpgsql
AS $function$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM public.tab_groups g
        INNER JOIN public.tab_group_members m ON m.group_id = g.group_id
        WHERE g.group_id = p_group_id
          AND g.org_id = p_org_id
          AND g.app_id = p_app_id
          AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND COALESCE(g.is_deleted, false) = false
          AND m.user_id = p_user_id::integer
          AND m.org_id = p_org_id
          AND m.app_id = p_app_id
          AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
          AND m.left_at IS NULL
          AND COALESCE(m.is_admin, false) = true
    ) THEN
        RAISE EXCEPTION 'fn_chat_assert_group_admin: user % is not an active admin of group %',
            p_user_id, p_group_id
            USING ERRCODE = '42501';
    END IF;
END;
$function$

