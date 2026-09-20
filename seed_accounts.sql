SET @bot_count = 20000;
SET @prefix    = 'stressbot_';   -- must match BotConfig.cs Prefix + '_'
SET @password  = 'test123';      -- must match BotConfig.cs Password
SET @min_width = 3;              -- must match BotConfig.cs AccountWidth

START TRANSACTION;

-- Row generator: produces integers 1..100000 via a cross join (no recursive
-- CTE / no server-specific extensions needed), then keeps the first @bot_count.
INSERT IGNORE INTO accounts (name, password, secret, type, premium_ends_at, email, creation)
SELECT
    CONCAT(@prefix, LPAD(n, GREATEST(@min_width, CHAR_LENGTH(CAST(n AS CHAR))), '0')),
    SHA1(@password),
    NULL,
    1,
    0,
    '',
    UNIX_TIMESTAMP()
FROM (
    SELECT (e.i * 10000 + d.i * 1000 + c.i * 100 + b.i * 10 + a.i) + 1 AS n
    FROM
        (SELECT 0 i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) a,
        (SELECT 0 i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) b,
        (SELECT 0 i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) c,
        (SELECT 0 i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) d,
        (SELECT 0 i UNION ALL SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3 UNION ALL SELECT 4 UNION ALL SELECT 5 UNION ALL SELECT 6 UNION ALL SELECT 7 UNION ALL SELECT 8 UNION ALL SELECT 9) e
) seq
WHERE n <= @bot_count;

-- One character per account, same name as the account (required for login).
-- Stats are deliberately generous (high mana/level/skills) so bots can walk,
-- attack, and spam the default "exevo gran mas flam" spell indefinitely
-- without dying to real monsters or running out of mana mid-benchmark.
-- vocation=1 assumes the standard OT convention (1=Sorcerer) so the spell is
-- castable -- adjust if this server defines vocations differently.
INSERT IGNORE INTO players (
    name, group_id, account_id, level, vocation,
    health, healthmax, mana, manamax, maglevel,
    lookbody, lookfeet, lookhead, looklegs, looktype, lookaddons,
    town_id, sex,
    skill_fist, skill_club, skill_sword, skill_axe, skill_dist, skill_shielding, skill_fishing
)
SELECT
    a.name, 1, a.id, 50, 1,
    4000, 4000, 999999, 999999, 20,
    114, 114, 128, 128, 128, 0,
    1, 0,
    60, 60, 60, 60, 60, 60, 60
FROM accounts a
WHERE a.name REGEXP CONCAT('^', @prefix, '[0-9]+$');

COMMIT;

-- Sanity check -- should show @bot_count for both once this finishes.
SELECT
    (SELECT COUNT(*) FROM accounts WHERE name REGEXP CONCAT('^', @prefix, '[0-9]+$')) AS accounts_created,
    (SELECT COUNT(*) FROM players  WHERE name REGEXP CONCAT('^', @prefix, '[0-9]+$')) AS characters_created;
