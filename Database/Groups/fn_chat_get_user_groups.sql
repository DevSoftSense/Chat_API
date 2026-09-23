-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.
-- Unread badges are computed in GroupRepository (not in this function).

DROP FUNCTION IF EXISTS public.fn_chat_get_user_groups(bigint, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_get_user_groups(
    p_user_id bigint,
    p_org_id integer,
    p_app_id integer,
    p_fiscal_year_id integer
)
 RETURNS TABLE(
    group_id bigint,
    chat_id integer,
    group_name character varying,
    group_code character varying,
    description character varying,
    created_by integer,
    created_at timestamp with time zone,
    is_admin boolean,
    is_active boolean,
    profile_pic character varying
)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    IF p_user_id IS NULL OR p_user_id <= 0
       OR p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_get_user_groups: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    RETURN QUERY
    SELECT
        g.group_id,
        g.chat_id,
        g.group_name,
        g.group_code,
        g.description,
        g.created_by,
        g.created_on,
        COALESCE(m.is_admin, false),
        (COALESCE(g.is_deleted, false) = false),
        g.profile_pic
    FROM public.tab_group_members m
    INNER JOIN public.tab_groups g ON g.group_id = m.group_id
    WHERE m.user_id = p_user_id::integer
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND m.left_at IS NULL
      AND g.org_id = p_org_id
      AND g.app_id = p_app_id
      AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND COALESCE(g.is_deleted, false) = false
    ORDER BY g.created_on DESC, g.group_id DESC;
END;
$function$
