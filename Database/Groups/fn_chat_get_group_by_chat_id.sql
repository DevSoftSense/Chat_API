-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

CREATE OR REPLACE FUNCTION public.fn_chat_get_group_by_chat_id(p_chat_id integer, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS TABLE(group_id bigint, chat_id integer, is_active boolean)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    IF p_chat_id IS NULL OR p_chat_id <= 0
       OR p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_get_group_by_chat_id: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT
        g.group_id,
        g.chat_id,
        (COALESCE(g.is_deleted, false) = false)
    FROM public.tab_groups g
    WHERE g.chat_id = p_chat_id
      AND g.org_id = p_org_id
      AND g.app_id = p_app_id
      AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
    LIMIT 1;
END;
$function$

