# Game Management temperature guide

The dashboard checks temperature once per second. This is display-only: it does not change fans, clocks, voltages, or thermal limits.

## Color meaning

| Part | Green — Safe | Orange — Warm | Red — Danger |
|---|---:|---:|---:|
| Intel Core i5-12450HX CPU | Below 80°C | 80–89°C | 90°C or above |
| NVIDIA RTX 3050 Laptop GPU | Below 75°C | 75–86°C | 87°C or above |

These are conservative warning bands for this laptop, not universal temperature ranges. Intel lists the i5-12450HX maximum junction temperature as 100°C. The installed NVIDIA driver reports an 87°C target temperature, a 97°C slowdown temperature, and a 100°C shutdown temperature.

If a value says **Unavailable**, Windows or Lenovo firmware did not expose that sensor to a normal-user program. Game Management deliberately does not install or use a privileged low-level hardware driver to bypass that restriction.

On this laptop, MSI Afterburner is the preferred CPU sensor provider. Game Management reads the `CPU temperature` entry only when Afterburner is already running. The connection is read-only and cannot apply a profile or change clocks, voltage, power limits, fans, or thermal settings.

## Sources

- Intel Core i5-12450HX specifications: https://www.intel.com/content/www/us/en/products/sku/228794/intel-core-i512450hx-processor-12m-cache-up-to-4-40-ghz/specifications.html
- Intel processor temperature guidance: https://www.intel.com/content/www/us/en/support/articles/000005597/processors.html
- NVIDIA `nvidia-smi` temperature definitions: https://docs.nvidia.com/deploy/nvidia-smi/
