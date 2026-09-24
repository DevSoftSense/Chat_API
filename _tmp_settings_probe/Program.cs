using Npgsql;
var cs = "Host=192.168.1.217;Port=5432;Database=SOC_SaaS_Product;Username=postgres;Password=soft123";
await using var conn = new NpgsqlConnection(cs);
await conn.OpenAsync();

Console.WriteLine("=== COLUMNS ===");
await using (var cmd = new NpgsqlCommand(@"
SELECT column_name, data_type FROM information_schema.columns
WHERE table_schema='public' AND table_name='tab_organisation_setting'
ORDER BY ordinal_position;", conn))
await using (var r = await cmd.ExecuteReaderAsync())
  while (await r.ReadAsync()) Console.WriteLine(r.GetString(0) + " | " + r.GetString(1));

Console.WriteLine("=== SAMPLE ROWS (Chat / Notification) ===");
await using (var cmd2 = new NpgsqlCommand(@"
SELECT setting_id, org_id, app_id, setting_key, setting_value, setting_group, data_type
FROM public.tab_organisation_setting
WHERE setting_group ILIKE '%chat%' OR setting_group ILIKE '%notif%'
   OR setting_key ILIKE '%chat%' OR setting_key ILIKE '%notif%' OR setting_key ILIKE '%message%'
   OR setting_key ILIKE '%announce%'
ORDER BY setting_group, setting_key
LIMIT 50;", conn))
await using (var r2 = await cmd2.ExecuteReaderAsync())
  while (await r2.ReadAsync())
    Console.WriteLine($"{r2.GetValue(0)} | org={r2.GetValue(1)} app={r2.GetValue(2)} | {r2.GetValue(3)}={r2.GetValue(4)} | group={r2.GetValue(5)} type={r2.GetValue(6)}");

Console.WriteLine("=== ALL GROUPS ===");
await using (var cmd3 = new NpgsqlCommand(@"
SELECT COALESCE(setting_group,'(null)'), COUNT(*) FROM public.tab_organisation_setting
GROUP BY 1 ORDER BY 1;", conn))
await using (var r3 = await cmd3.ExecuteReaderAsync())
  while (await r3.ReadAsync()) Console.WriteLine(r3.GetString(0) + " => " + r3.GetInt64(1));
