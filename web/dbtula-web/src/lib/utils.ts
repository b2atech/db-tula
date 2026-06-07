import { type ClassValue, clsx } from "clsx"
import { twMerge } from "tailwind-merge"

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

/** Smooth-scroll the landing page to the hero section (where the Google sign-in button lives). */
export function scrollToHero() {
  document.getElementById('hero')?.scrollIntoView({ behavior: 'smooth' })
}
