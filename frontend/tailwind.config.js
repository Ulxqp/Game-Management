/** @type {import('tailwindcss').Config} */
export default {
  darkMode: ["class"],
  content: ["./index.html", "./src/**/*.{ts,tsx}"],
  theme: {
    extend: {
      fontFamily: { sans: ["Segoe UI", "Inter", "system-ui", "sans-serif"] },
      colors: {
        border: "hsl(var(--border))", background: "hsl(var(--background))",
        foreground: "hsl(var(--foreground))", muted: "hsl(var(--muted))",
        "muted-foreground": "hsl(var(--muted-foreground))",
        primary: "hsl(var(--primary))", "primary-foreground": "hsl(var(--primary-foreground))",
        destructive: "hsl(var(--destructive))"
      },
      borderRadius: { card: "14px", panel: "16px" },
      boxShadow: { card: "0 1px 2px rgba(15,23,42,.04), 0 8px 24px rgba(15,23,42,.06)" }
    }
  },
  plugins: []
};
