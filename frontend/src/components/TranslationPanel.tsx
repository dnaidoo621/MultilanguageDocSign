"use client";

import type { TranslationDetail, TranslationSegmentDto } from "@/lib/types";

export default function TranslationPanel({
  translation,
  activeBlock,
  onHover,
}: {
  translation: TranslationDetail;
  activeBlock: string | null;
  onHover: (id: string | null) => void;
}) {
  // Group segments by page, preserving order.
  const pages = new Map<number, TranslationSegmentDto[]>();
  for (const s of translation.segments) {
    const arr = pages.get(s.pageNumber) ?? [];
    arr.push(s);
    pages.set(s.pageNumber, arr);
  }

  return (
    <div className="flex flex-col gap-4">
      {[...pages.entries()].map(([pageNum, segs]) => (
        <div key={pageNum} className="flex flex-col gap-1.5">
          <p className="text-xs font-medium uppercase tracking-wide text-gray-400">
            Page {pageNum}
          </p>
          {segs.map((s) => (
            <div
              key={s.sourceBlockId}
              data-testid="translation-segment"
              onMouseEnter={() => onHover(s.sourceBlockId)}
              onMouseLeave={() => onHover(null)}
              className={`cursor-default rounded-md border p-2.5 transition-colors ${
                activeBlock === s.sourceBlockId
                  ? "border-blue-500 bg-blue-50"
                  : "border-gray-200 bg-white hover:border-gray-300"
              }`}
            >
              <p className="text-sm text-gray-900">{s.translatedText}</p>
              <p className="mt-1 border-t border-gray-100 pt-1 text-xs text-gray-400">
                {s.sourceText}
              </p>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}
