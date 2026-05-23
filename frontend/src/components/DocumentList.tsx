"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { apiJson } from "@/lib/api";
import type { DocumentSummary } from "@/lib/types";

const statusColor: Record<string, string> = {
  Uploaded: "bg-gray-100 text-gray-700",
  Processing: "bg-amber-100 text-amber-800",
  Extracted: "bg-green-100 text-green-800",
  Failed: "bg-red-100 text-red-800",
};

export function DocumentList({ refreshKey }: { refreshKey: number }) {
  const [docs, setDocs] = useState<DocumentSummary[]>([]);
  const [error, setError] = useState<string | null>(null);
  const timer = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(async () => {
    try {
      setDocs(await apiJson<DocumentSummary[]>("/api/documents"));
      setError(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to load documents.");
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load, refreshKey]);

  // Poll while any document is still processing.
  useEffect(() => {
    const anyPending = docs.some(
      (d) => d.status === "Uploaded" || d.status === "Processing",
    );
    if (anyPending && !timer.current) {
      timer.current = setInterval(() => void load(), 3000);
    } else if (!anyPending && timer.current) {
      clearInterval(timer.current);
      timer.current = null;
    }
    return () => {
      if (timer.current) {
        clearInterval(timer.current);
        timer.current = null;
      }
    };
  }, [docs, load]);

  if (error) return <p className="text-sm text-red-600">{error}</p>;
  if (docs.length === 0)
    return <p className="text-sm text-gray-500">No documents yet. Upload one to get started.</p>;

  return (
    <ul className="divide-y divide-gray-200 rounded-lg border border-gray-200">
      {docs.map((d) => (
        <li key={d.id} className="flex items-center justify-between gap-4 px-4 py-3">
          <div className="min-w-0">
            <p className="truncate text-sm font-medium">{d.fileName}</p>
            <p className="text-xs text-gray-500">
              {d.pageCount > 0 ? `${d.pageCount} pages` : "—"}
              {d.sourceLanguage ? ` · ${d.sourceLanguage}` : ""}
            </p>
          </div>
          <div className="flex shrink-0 items-center gap-3">
            <span
              className={`rounded-full px-2 py-0.5 text-xs ${statusColor[d.status] ?? "bg-gray-100 text-gray-700"}`}
            >
              {d.status}
            </span>
            {d.status === "Extracted" && (
              <Link href={`/documents/${d.id}`} className="text-sm text-blue-600 underline">
                View
              </Link>
            )}
          </div>
        </li>
      ))}
    </ul>
  );
}
