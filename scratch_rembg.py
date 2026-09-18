from rembg import remove
from PIL import Image

input_path = 'Assets/dinodesk_icon.jpg'
output_path = 'Assets/dinodesk_icon.png'

print("Opening image...")
input_image = Image.open(input_path)

print("Removing background...")
output_image = remove(input_image)

print("Saving transparent PNG...")
output_image.save(output_path)
print("Done!")
