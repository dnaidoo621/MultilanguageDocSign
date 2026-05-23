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

      <div className="rounded-lg border border-gray-200 px-4 py-2 text-xs text-gray-400">
        Phase 0 scaffold · Next.js + Supabase auth · .NET 10 modular monolith backend
      </div>

      <p className="max-w-md text-xs text-gray-400">
        This is an AI-assisted comprehension tool, not certified legal
        translation or legal advice.
      </p>
    </main>
  );
}
