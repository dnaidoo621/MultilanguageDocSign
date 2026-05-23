export default function Home() {
  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-6 p-8 text-center">
      <div>
        <h1 className="text-4xl font-bold tracking-tight">LinguaSign</h1>
        <p className="mt-3 max-w-md text-balance text-sm text-gray-500">
          Understand and confidently sign multilingual documents. Upload a
          document, review a synchronized translation with risk highlights, then
          sign — with a full audit trail.
        </p>
      </div>

      <a
        href="/dashboard"
        className="rounded-full bg-black px-5 py-2.5 text-sm font-medium text-white hover:bg-gray-800"
      >
        Get started
      </a>

      <div className="rounded-lg border border-gray-200 px-4 py-2 text-xs text-gray-400">
        Phase 1 · upload + OCR · Next.js + Supabase · .NET 10 backend
      </div>

      <p className="max-w-md text-xs text-gray-400">
        This is an AI-assisted comprehension tool, not certified legal
        translation or legal advice.
      </p>
    </main>
  );
}
