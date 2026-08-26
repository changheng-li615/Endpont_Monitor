import NextAuth from "next-auth";
import Google from "next-auth/providers/google";
import { getManagerAuthMode, isAllowedGoogleManager } from "@/lib/manager-auth";

const isGoogleMode = getManagerAuthMode() === "google";
const googleClientId = process.env.XUGAR_MANAGER_GOOGLE_CLIENT_ID;
const googleClientSecret = process.env.XUGAR_MANAGER_GOOGLE_CLIENT_SECRET;

if (isGoogleMode && (!googleClientId || !googleClientSecret || !process.env.AUTH_SECRET)) {
  throw new Error("Google manager authentication is enabled but required credentials are missing.");
}

export const { handlers, auth, signIn, signOut } = NextAuth({
  secret: process.env.AUTH_SECRET,
  session: { strategy: "jwt" },
  providers: isGoogleMode
    ? [Google({ clientId: googleClientId!, clientSecret: googleClientSecret! })]
    : [],
  callbacks: {
    async signIn({ profile }) {
      const emailVerified = (profile as { email_verified?: boolean } | undefined)?.email_verified === true;
      return isGoogleMode && emailVerified && isAllowedGoogleManager(profile?.email);
    },
  },
});
