SELECT id, name
FROM accounts
WHERE NOT is_archived
ORDER BY name, id;
