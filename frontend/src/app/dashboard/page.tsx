"use client";

import { useState } from "react";
import { useUser } from "@/lib/useUser";
import { createClient } from "@/lib/supabase/client";
import { AuthForm } from "@/components/AuthForm";
import { UploadDropzone } from "@/components/UploadDropzone";
import { DocumentList } from "@/components/DocumentList";

export default function DashboardPage() {
  const { user, loading } = useUser();
  const [refreshKey, setRefreshKey] = useState(0);

  if (loading) {
    return <main className="p-8 text-sm text-gray-500">Loading…</main>;
  }

  if (!user) {
    return (
      <main className="flex min-h-screen items-center justify-center p-8">
        <AuthForm />
      </main>
    );
  }

  return (
    <main className="mx-auto flex max-w-2xl flex-col gap-6 p-8">
      <header className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Your documents</h1>
        <div className="flex items-center gap-3 text-sm text-gray-500">
          <span>{user.email}</span>
          <button
            onClick={() => void createClient().auth.signOut()}
            className="underline"
          >
            Sign out
          </button>
        </div>
      </header>

      <UploadDropzone onUploaded={() => setRefreshKey((k) => k + 1)} />
      <DocumentList refreshKey={refreshKey} />
    </main>
  );
}
