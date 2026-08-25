import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: "../backend/FantasyAnalysis.Api/wwwroot",
    emptyOutDir: true
  }
});
