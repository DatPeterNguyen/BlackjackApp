-- ============================================================================
--  Online leaderboard - Supabase schema
-- ============================================================================
--
--  HOW TO APPLY
--    1. Create a free project at https://supabase.com
--    2. Open the project's SQL Editor and run this whole file
--    3. Copy Project Settings -> API -> "Project URL" and the "anon public"
--       key into BlackjackApp.Maui/Services/LeaderboardConfig.cs
--
--  Until step 3 is done the app shows its local leaderboard instead. Nothing
--  errors; the online board simply is not there.
--
-- ----------------------------------------------------------------------------
--  READ THIS BEFORE SHIPPING IT
--
--  The app authenticates with the anon key, which is public by design - it
--  ships inside the app and anyone can pull it out of a build. There is no
--  per-player login here, so the server has NO WAY to tell a real score from
--  an invented one. Someone who extracts the key can post any balance they
--  like under an id of their own making.
--
--  What the policies below DO prevent:
--    - lowering somebody else's score (the trigger keeps the higher value,
--      so a score can only ever go up)
--    - deleting other players' rows (no delete policy exists at all)
--    - absurd values polluting the ranking (a very high CHECK ceiling, set
--      where no honest player can reach it - see the constraint's own note)
--
--  What they CANNOT prevent, and no amount of SQL here will:
--    - a determined player claiming a score they did not earn
--    - overwriting somebody else's player_name; the trigger guards the
--      score columns only, and without a login there is nothing to check
--      a name change against
--
--  That is an acceptable trade for a casual game with imaginary money, as
--  long as it is a known one. If the board ever needs to be trustworthy, the
--  fix is Supabase anonymous sign-in: every install gets a real JWT, and the
--  policies below become `auth.uid() = player_id`, which the server CAN
--  verify. That is a client change too, so it is deliberately not pretended
--  at here.
-- ============================================================================

create table if not exists public.leaderboard (
    player_id    uuid        primary key,
    player_name  text        not null,
    best_balance numeric     not null,
    achieved_at  timestamptz not null default now(),
    updated_at   timestamptz not null default now(),

    -- Matches GameProgressStorage.MaxOnlinePlayerNameLength, so the client
    -- and the server agree on what a name may be rather than the server
    -- silently rejecting what the client happily accepted.
    constraint leaderboard_name_length check (char_length(player_name) between 1 and 24),

    -- Deliberately enormous. There is no legitimate ceiling on a balance -
    -- the $10,000 cap is on a single BET, and a long winning run compounds
    -- well past it - so any cap chosen to look "reasonable" would eventually
    -- start silently rejecting real players' scores. This exists only to keep
    -- the ranking from being buried under a fabricated number with twenty
    -- digits in it, and is not pretending to be an anti-cheat measure.
    constraint leaderboard_balance_range check (best_balance >= 0 and best_balance <= 1000000000)
);

-- Ranking reads are always "highest first", so give them an index that
-- answers in that order rather than sorting the table on every open.
create index if not exists leaderboard_best_balance_idx
    on public.leaderboard (best_balance desc, achieved_at asc);

-- ----------------------------------------------------------------------------
--  Scores only ever go up.
--
--  The client upserts the player's best balance. Without this, a stale or
--  malicious client could overwrite a high score with a lower one - including
--  somebody else's. Clamping here rather than rejecting means an out-of-date
--  client's submission is harmless instead of an error it cannot recover from.
-- ----------------------------------------------------------------------------
create or replace function public.leaderboard_scores_only_go_up()
returns trigger
language plpgsql
as $$
begin
    -- <= not <: an identical score must not refresh achieved_at either,
    -- because that column is the tie-break (see the ordering index) and
    -- refreshing it drops the player below everyone they were level with.
    if new.best_balance <= old.best_balance then
        new.best_balance := old.best_balance;
        new.achieved_at  := old.achieved_at;
    end if;

    new.updated_at := now();
    return new;
end;
$$;

drop trigger if exists leaderboard_monotonic on public.leaderboard;

create trigger leaderboard_monotonic
    before update on public.leaderboard
    for each row
    execute function public.leaderboard_scores_only_go_up();

-- ----------------------------------------------------------------------------
--  Row level security
--
--  RLS is on with no delete policy, which means deletes are refused outright -
--  a policy that does not exist denies by default.
-- ----------------------------------------------------------------------------
alter table public.leaderboard enable row level security;

drop policy if exists "leaderboard is public" on public.leaderboard;
create policy "leaderboard is public"
    on public.leaderboard for select
    using (true);

drop policy if exists "anyone may claim a row" on public.leaderboard;
create policy "anyone may claim a row"
    on public.leaderboard for insert
    with check (true);

-- Updates are allowed, but the trigger above means they can only raise a
-- score, never lower one.
drop policy if exists "anyone may raise a score" on public.leaderboard;
create policy "anyone may raise a score"
    on public.leaderboard for update
    using (true)
    with check (true);
