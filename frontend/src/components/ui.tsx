import * as React from "react";
import * as AlertDialogPrimitive from "@radix-ui/react-alert-dialog";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "../lib/utils";

const buttonVariants = cva("inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-[10px] text-[13px] font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#93c5fd] disabled:pointer-events-none disabled:opacity-45", {
  variants: { variant: {
    default: "bg-[#2563eb] text-white hover:bg-[#1d4ed8]",
    game: "bg-[#ef4444] text-white hover:bg-[#dc2626]",
    outline: "border border-[#e2e8f0] bg-white text-[#0f172a] hover:bg-[#f1f5f9]",
    ghost: "text-[#475569] hover:bg-[#f1f5f9] hover:text-[#0f172a]",
    danger: "bg-[#fee2e2] text-[#b91c1c] hover:bg-[#fecaca]"
  }, size: { default: "h-9 px-4", sm: "h-8 px-3 text-xs", icon: "h-9 w-9 p-0", timer: "h-[42px] min-w-[124px] px-5" } },
  defaultVariants: { variant: "default", size: "default" }
});
export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement>, VariantProps<typeof buttonVariants> {}
export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(({ className, variant, size, ...props }, ref) => <button ref={ref} className={cn(buttonVariants({ variant, size }), className)} {...props} />);
Button.displayName = "Button";

export function Card({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) { return <div className={cn("rounded-card border border-[#e2e8f0] bg-white shadow-card", className)} {...props} />; }
export function CardHeader({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) { return <div className={cn("flex items-center justify-between px-4 pt-4", className)} {...props} />; }
export function CardContent({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) { return <div className={cn("p-4", className)} {...props} />; }
export function Badge({ className, ...props }: React.HTMLAttributes<HTMLSpanElement>) { return <span className={cn("inline-flex h-[26px] items-center rounded-full bg-[#f1f5f9] px-2.5 text-xs font-semibold text-[#475569]", className)} {...props} />; }

export const AlertDialog = AlertDialogPrimitive.Root;
export const AlertDialogTrigger = AlertDialogPrimitive.Trigger;
export function AlertDialogContent({ className, ...props }: React.ComponentPropsWithoutRef<typeof AlertDialogPrimitive.Content>) {
  return <AlertDialogPrimitive.Portal><AlertDialogPrimitive.Overlay className="fixed inset-0 z-[90] bg-slate-950/25 backdrop-blur-[1px]"/><AlertDialogPrimitive.Content className={cn("fixed left-1/2 top-1/2 z-[100] w-[min(420px,calc(100vw-32px))] -translate-x-1/2 -translate-y-1/2 rounded-panel border border-[#e2e8f0] bg-white p-6 shadow-2xl", className)} {...props}/></AlertDialogPrimitive.Portal>;
}
export const AlertDialogTitle = ({ className, ...props }: React.ComponentPropsWithoutRef<typeof AlertDialogPrimitive.Title>) => <AlertDialogPrimitive.Title className={cn("text-base font-bold text-[#0f172a]", className)} {...props}/>;
export const AlertDialogDescription = ({ className, ...props }: React.ComponentPropsWithoutRef<typeof AlertDialogPrimitive.Description>) => <AlertDialogPrimitive.Description className={cn("mt-2 text-sm leading-6 text-[#64748b]", className)} {...props}/>;
export const AlertDialogCancel = React.forwardRef<HTMLButtonElement, React.ComponentPropsWithoutRef<typeof AlertDialogPrimitive.Cancel>>(({ className, ...props }, ref) => <AlertDialogPrimitive.Cancel ref={ref} className={cn(buttonVariants({ variant: "outline" }), className)} {...props}/>);
export const AlertDialogAction = React.forwardRef<HTMLButtonElement, React.ComponentPropsWithoutRef<typeof AlertDialogPrimitive.Action>>(({ className, ...props }, ref) => <AlertDialogPrimitive.Action ref={ref} className={cn(buttonVariants({ variant: "default" }), className)} {...props}/>);

export function Field({ label, children, hint }: { label: string; children: React.ReactNode; hint?: string }) { return <label className="grid gap-1.5 text-xs font-semibold text-[#475569]"><span>{label}</span>{children}{hint && <span className="font-normal text-[#94a3b8]">{hint}</span>}</label>; }
export const inputClass = "h-9 w-full rounded-[10px] border border-[#cbd5e1] bg-white px-3 text-[13px] text-[#0f172a] outline-none focus:border-[#93c5fd] focus:ring-2 focus:ring-[#dbeafe]";
