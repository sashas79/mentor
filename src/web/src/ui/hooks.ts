import { useCallback, useEffect, useRef, useState } from "react";
import { ApiProblem } from "@/api/client";

function asProblem(error: unknown): ApiProblem {
  return error instanceof ApiProblem ? error : new ApiProblem(0, "The mentorship service could not be reached", undefined, {});
}

export interface Loaded<T> {
  data?: T;
  problem?: ApiProblem;
  loading: boolean;
  reload(): Promise<void>;
}

/** Reads what a screen shows, again whenever <paramref name="key"/> changes or the screen asks. */
export function useLoad<T>(load: () => Promise<T>, key: unknown[]): Loaded<T> {
  const [data, setData] = useState<T>();
  const [problem, setProblem] = useState<ApiProblem>();
  const [loading, setLoading] = useState(true);
  const latest = useRef(load);
  latest.current = load;
  const generation = useRef(0);

  const reload = useCallback(async () => {
    const mine = ++generation.current;
    setLoading(true);
    try {
      const value = await latest.current();
      if (mine === generation.current) {
        setData(value);
        setProblem(undefined);
      }
    } catch (error) {
      if (mine === generation.current) {
        setProblem(asProblem(error));
      }
    } finally {
      if (mine === generation.current) {
        setLoading(false);
      }
    }
  }, []);

  useEffect(() => {
    void reload();
    // The key lists what the load depends on; the load itself is always the latest one.
  }, key);

  return { data, problem, loading, reload };
}

export interface Command {
  pending: boolean;
  problem?: ApiProblem;
  /** What the last command achieved, in words, until the next one runs. */
  done?: string;
  /**
   * Runs a command against the server and reports its outcome. When the server answers 409, the record changed since it
   * was read or cannot take this command now: <paramref name="reread"/> reads it again so the person decides on its
   * current state (ADR-005, ADR-012).
   */
  run(command: () => Promise<unknown>, outcome: string, reread?: () => Promise<void>): Promise<boolean>;
  clear(): void;
}

export function useCommand(): Command {
  const [pending, setPending] = useState(false);
  const [problem, setProblem] = useState<ApiProblem>();
  const [done, setDone] = useState<string>();

  const run = useCallback(async (command: () => Promise<unknown>, outcome: string, reread?: () => Promise<void>) => {
    setPending(true);
    setProblem(undefined);
    setDone(undefined);
    try {
      await command();
      setDone(outcome);
      return true;
    } catch (error) {
      const refused = asProblem(error);
      setProblem(refused);
      if (refused.isConflict && reread) {
        await reread();
      }

      return false;
    } finally {
      setPending(false);
    }
  }, []);

  const clear = useCallback(() => {
    setProblem(undefined);
    setDone(undefined);
  }, []);

  return { pending, problem, done, run, clear };
}
