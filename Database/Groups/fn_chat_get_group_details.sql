-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

DROP FUNCTION IF EXISTS public.fn_chat_get_group_details(bigint, bigint, integer, integer, integer);

CREATE OR REPLACE FUNCTION public.fn_chat_get_group_details(
    p_group_id bigint,
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
    is_active boolean,
    created_at timestamp with time zone,
    profile_pic character varying,
    member_user_id integer,
    member_is_admin boolean,
    member_joined_at timestamp with time zone,
    member_is_active boolean
)
 LANGUAGE plpgsql
 STABLE
AS $function$
BEGIN
    IF p_group_id IS NULL OR p_group_id <= 0
       OR p_user_id IS NULL OR p_user_id <= 0
       OR p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_get_group_details: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    PERFORM public.fn_chat_assert_group_member(
        p_group_id, p_user_id, p_org_id, p_app_id, p_fiscal_year_id);

    RETURN QUERY
    SELECT
        g.group_id,
        g.chat_id,
        g.group_name,
        g.group_code,
        g.description,
        g.created_by,
        (COALESCE(g.is_deleted, false) = false),
        g.created_on,
        g.profile_pic,
        m.user_id,
        COALESCE(m.is_admin, false),
        m.joined_on,
        (m.left_at IS NULL)
    FROM public.tab_groups g
    INNER JOIN public.tab_group_members m ON m.group_id = g.group_id
    WHERE g.group_id = p_group_id
      AND g.org_id = p_org_id
      AND g.app_id = p_app_id
      AND g.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND m.org_id = p_org_id
      AND m.app_id = p_app_id
      AND m.fiscal_year_id IS NOT DISTINCT FROM p_fiscal_year_id
      AND m.left_at IS NULL
    ORDER BY COALESCE(m.is_admin, false) DESC, m.joined_on ASC, m.user_id ASC;
END;
$function$
