import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // Emit a self-contained server build for slim container images.
  output: "standalone",
};

export default nextConfig;
