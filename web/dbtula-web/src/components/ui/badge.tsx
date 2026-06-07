import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "../../lib/utils"

const badgeVariants = cva(
  "inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-semibold transition-colors",
  {
    variants: {
      variant: {
        default: "border-transparent bg-orange-100 text-orange-700 dark:bg-brand-orange/15 dark:text-brand-orange-soft",
        secondary: "border-transparent bg-slate-100 text-slate-700 dark:bg-bg-elevated dark:text-text-secondary",
        destructive: "border-transparent bg-red-100 text-red-700 dark:bg-red-500/15 dark:text-red-400",
        success: "border-transparent bg-green-100 text-green-700 dark:bg-green-500/15 dark:text-green-400",
        warning: "border-transparent bg-amber-100 text-amber-700 dark:bg-amber-500/15 dark:text-amber-400",
        outline: "text-slate-700 border-slate-200 dark:text-text-secondary dark:border-border-soft",
        running: "border-transparent bg-blue-100 text-blue-700 animate-pulse dark:bg-blue-500/15 dark:text-blue-400",
      },
    },
    defaultVariants: { variant: "default" },
  }
)

export interface BadgeProps extends React.HTMLAttributes<HTMLDivElement>, VariantProps<typeof badgeVariants> {}

function Badge({ className, variant, ...props }: BadgeProps) {
  return <div className={cn(badgeVariants({ variant }), className)} {...props} />
}

export { Badge, badgeVariants }
