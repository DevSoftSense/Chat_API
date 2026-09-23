-- Synced from live DB (SOC_SaaS_Product). Do not recreate tables.
-- DAY 1 Group Chat — additive; One-to-One paths unchanged.

CREATE OR REPLACE FUNCTION public.fn_chat_add_group_members(p_group_id bigint, p_member_user_ids bigint[], p_requesting_user_id bigint, p_org_id integer, p_app_id integer, p_fiscal_year_id integer)
 RETURNS TABLE(group_id bigint, user_id integer, is_admin boolean, joined_at timestamp with time zone, is_active boolean, reactivated boolean)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_uid bigint;
    v_joined timestamptz;
    v_reactivated boolean;
BEGIN
    IF p_group_id IS NULL OR p_group_id <= 0
       OR p_requesting_user_id IS NULL OR p_requesting_user_id <= 0
       OR p_org_id IS NULL OR p_app_id IS NULL OR p_fiscal_year_id IS NULL THEN
        RAISE EXCEPTION 'fn_chat_add_group_members: required parameters cannot be null'
            USING ERRCODE = '22023';
    END IF;

    IF p_member_user_ids IS NULL OR cardinality(p_member_user_ids) = 0 THEN
        RAISE EXCEPTION 'fn_chat_add_group_members: member_user_ids is required'
            USING ERRCODE = '22023';
    END IF;

    PERFORM public.fn_chat_assert_group_admin(
        p_group_id, p_requesting_user_id, p_org_id, p_app_id, p_fiscal_year_id);

    FOREACH v_uid IN ARRAY p_member_user_ids
    LOOP
        IF v_uid IS NULL OR v_uid <= 0 THEN
            CONTINUE;
        END IF;

        IF v_uid > 2147483647 THEN
            RAISE EXCEPTION 'fn_chat_add_group_members: user_id % exceeds integer range', v_uid
                USING ERRCODE = '22023';
        END IF;

        -- Already active → skip (no duplicate).
        IF EXISTS (
            SELECT 1
            FROM public.tab_group_members m
            WHERE m.group_id = p_group_id
              AND m.user_id = v_uid::integer
              AND m.left_at IS NULL
        ) THEN
            CONTINUE;
        END IF;

        v_joined := CURRENT_TIMESTAMP;
        v_reactivated := EXISTS (
            SELECT 1
            FROM public.tab_group_members m
            WHERE m.group_id = p_group_id
              AND m.user_id = v_uid::integer
              AND m.left_at IS NOT NULL
        );

        INSERT INTO public.tab_group_members (
            group_id,
            user_id,
            app_id,
            joined_on,
            left_at,
            is_admin,
            org_id,
            fiscal_year_id
        )
        VALUES (
            p_group_id,
            v_uid::integer,
            p_app_id,
            v_joined,
            NULL,
            false,
            p_org_id,
            p_fiscal_year_id
        )
        ON CONFLICT ON CONSTRAINT tab_group_members_group_id_user_id_key DO UPDATE
        SET left_at = NULL,
            is_admin = false,
            joined_on = v_joined,
            app_id = EXCLUDED.app_id,
            org_id = EXCLUDED.org_id,
            fiscal_year_id = EXCLUDED.fiscal_year_id;

        group_id := p_group_id;
        user_id := v_uid::integer;
        is_admin := false;
        joined_at := v_joined;
        is_active := true;
        reactivated := v_reactivated;
        RETURN NEXT;
    END LOOP;
END;
$function$

